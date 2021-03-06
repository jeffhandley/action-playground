using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using static Octokit.GraphQL.Variable;
using Env = System.Environment;
using AreaPod.IssueTriage.Models;
using System.Linq;
using AreaPod.IssueTriage.Rules;

namespace AreaPod.IssueTriage;

internal class Program
{
    static string GITHUB_ACTOR => Env.GetEnvironmentVariable("GITHUB_ACTOR")!;
    static string GITHUB_TOKEN => Env.GetEnvironmentVariable("GITHUB_TOKEN")!;

    static async Task<int> Main(string[] args)
    {
        string? GITHUB_ACTOR = Env.GetEnvironmentVariable("GITHUB_ACTOR");
        string? GITHUB_TOKEN = Env.GetEnvironmentVariable("GITHUB_TOKEN");

        if (string.IsNullOrWhiteSpace(GITHUB_ACTOR) || string.IsNullOrWhiteSpace(GITHUB_TOKEN))
        {
            throw new ArgumentException("Missing environment variable. GITHUB_ACTOR and GITHUB_TOKEN are required");
        }

        var issueArg = new Option<uint>(new[] { "--issue", "-i" }, "The issue number to process") { IsRequired = true };
        var actionArg = new Option<IssueAction?>(new[] { "--action", "-a" }, "The issue action");
        var assigneeArg = new Option<string?>("--assignee", "The assignee added or removed");
        var labelArg = new Option<string?>("--label", "The label added or removed");

        var triageCommand = new RootCommand("Area Pod issue triage") { issueArg, actionArg, assigneeArg, labelArg };
        triageCommand.SetHandler(HandleIssueTriage, issueArg, actionArg, assigneeArg, labelArg);

        return await triageCommand.InvokeAsync(args);
    }

    static async Task HandleIssueTriage(uint issueNumber, IssueAction? action, string? assignee, string? label)
    {
        var issueEvent = new IssueEvent
        {
            User = GITHUB_ACTOR,
            Action = action,
            Assignee = assignee,
            Label = label
        };

        Console.WriteLine($"Handling Issue Event");
        Console.WriteLine($"  Issue number: {issueNumber}");
        Console.WriteLine($"  User:         {issueEvent.User}");
        Console.WriteLine($"  Action:       {issueEvent.Action}");
        Console.WriteLine($"  Assignee:     {issueEvent.Assignee}");
        Console.WriteLine($"  Label:        {issueEvent.Label}");

        var appInfo = new ProductHeaderValue("AreaPod.IssueTriage");
        var connection = new Connection(appInfo, GITHUB_TOKEN);

        var query = new Query()
            .RepositoryOwner(Var("owner"))
            .Repository(Var("repo"))
            .Issue(Var("issue_number"))
            .Select(issue => new IssueForTriage
            {
                Id = issue.Id,
                Number = issue.Number,
                Closed = issue.Closed,
                Milestone = issue.Milestone.Select(milestone => milestone.Title).SingleOrDefault(),
                Labels = issue
                    .Labels(null, null, null, null, new LabelOrder { Field = LabelOrderField.Name, Direction = OrderDirection.Asc })
                    .AllPages()
                    .Select(label => label.Name)
                    .ToList(),
                Author = issue.Author.Select(author => author.Login).Single()!,
                AuthorAssociation = issue.AuthorAssociation,
                ProjectCards_v1 = issue
                    .ProjectCards(null, null, null, null, null)
                    .AllPages()
                    .Select(card => new ProjectCard_v1
                    {
                        Id = card.Id,
                        ProjectName = card.Project.Name,
                        ProjectNumber = card.Project.Number,
                        ColumnId = card.Column.Select(column => column.Id).SingleOrDefault(),
                        ColumnName = card.Column.Select(column => column.Name).SingleOrDefault(),
                        IsArchived = card.IsArchived,
                    })
                    .ToList(),
            }).Compile();

        var values = new Dictionary<string, object>
        {
            { "owner", "jeffhandley" },
            { "repo", "action-playground" },
            { "issue_number", issueNumber }
        };

        var issue = await connection.Run(query, values);

        Console.WriteLine($"Issue {issue.Number} was {action?.ToString() ?? "processed"}");
        Console.WriteLine($"  State: {(issue.Closed ? "Closed" : "Open")}");
        Console.WriteLine($"  Milestone: {issue.Milestone ?? "<none>"}");
        Console.WriteLine($"  Labels: {string.Join(", ", issue.Labels)}");
        Console.WriteLine($"  Author: {issue.Author}");
        Console.WriteLine($"  Author Association: {issue.AuthorAssociation.ToString()}");
        
        foreach (var pc in issue.ProjectCards_v1)
        {
            Console.WriteLine($"  On Project '{pc.ProjectName}' ({pc.ProjectNumber}) in Column '{pc.ColumnName}'{(pc.IsArchived ? " [Archive]" : "")}");
        }

        bool addNeedsTriageLabel = IssueTriageRules.ShouldAddNeedsTriageLabel(issueEvent, issue);
        bool removeNeedsTriageLabel = !addNeedsTriageLabel && IssueTriageRules.ShouldRemoveTriageNeededLabel(issueEvent, issue);

        if (addNeedsTriageLabel || removeNeedsTriageLabel)
        {
            var needsTriageLabelQuery = new Query()
                .Repository("action-playground", "jeffhandley", true)
                .Label("untriaged")
                .Select(label => label.Id)
                .Compile();

            ID needsTriageLabelId = await connection.Run(needsTriageLabelQuery);

            if (string.IsNullOrEmpty(needsTriageLabelId.Value))
            {
                Console.WriteLine($"  Label '{IssueTriageRules.NeedsTriageLabel}' could not be found. Aborting.");
            }
            else
            {
                if (addNeedsTriageLabel)
                {
                    var needsTriage = new Mutation()
                        .AddLabelsToLabelable(new AddLabelsToLabelableInput { LabelableId = issue.Id, LabelIds = new[] { needsTriageLabelId } })
                        .Select(result => result.ClientMutationId)
                        .Compile();

                    await connection.Run(needsTriage);

                    Console.WriteLine($"  Issue triage is needed. {IssueTriageRules.NeedsTriageLabel} was added.");
                }
                else
                {
                    var triageCompleted = new Mutation()
                        .RemoveLabelsFromLabelable(new RemoveLabelsFromLabelableInput { LabelableId = issue.Id, LabelIds = new[] { needsTriageLabelId } })
                        .Select(result => result.ClientMutationId)
                        .Compile();

                    await connection.Run(triageCompleted);

                    Console.WriteLine($"  Issue triage completed. {IssueTriageRules.NeedsTriageLabel} was removed.");
                }
            }
        }
        else
        {
            Console.WriteLine("  Issue triage unaffected.");
        }
    }
}
