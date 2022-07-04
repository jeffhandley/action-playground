﻿using System.Collections.Generic;
using Octokit.GraphQL.Model;
using AreaPod.IssueTriage.Rules;

namespace AreaPod.IssueTriage.Models;

internal class IssueForTriage
{
    public int Number { get; init; }
    public bool Closed { get; init; }
    public string? Milestone { get; init; }

#nullable disable
    public List<string> Labels { get; init; }
    public string Author { get; init; }
#nullable restore

    public CommentAuthorAssociation? AuthorAssociation { get; init; }

    public bool NeedsTriage => IssueTriageRules.NeedsTriage(this);
}
