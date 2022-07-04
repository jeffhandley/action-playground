﻿#nullable disable warnings

using Octokit.GraphQL;

namespace AreaPod.IssueTriage.Models;

internal class ProjectCardClassic
{
    public ID Id { get; set; }
    public int ProjectNumber { get; init; }
    public string ProjectName { get; init; }
    public ID? ColumnId { get; init; }
    public string? ColumnName { get; init; }
    public bool IsArchived { get; init; }
}
