namespace SaasEngine.Contracts.Audit;

/// <summary>A paginated result set with cursor-based pagination.</summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Gets the items in this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Gets the cursor for the next page, or null if this is the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Gets whether there are more results.</summary>
    public bool HasMore => NextCursor is not null;
}
