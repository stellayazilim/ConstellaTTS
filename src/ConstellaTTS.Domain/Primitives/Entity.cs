namespace ConstellaTTS.Domain.Primitives;

/// <summary>
/// Base class for all domain entities.
/// Equality is based on identity components, not reference.
/// </summary>
public abstract class Entity<TId>(TId id) : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; } = id;

    public abstract IEnumerable<object> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return GetEqualityComponents()
            .SequenceEqual(((Entity<TId>)obj).GetEqualityComponents());
    }

    public bool Equals(Entity<TId>? other) => Equals((object?)other);

    public static bool operator ==(Entity<TId> left, Entity<TId> right) =>  Equals(left, right);
    public static bool operator !=(Entity<TId> left, Entity<TId> right) => !Equals(left, right);

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
}
