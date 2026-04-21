namespace ConstellaTTS.Domain.Primitives;

/// <summary>
/// Base class for engine-specific section model parameters.
/// ID is the concrete model type — one model instance per type.
/// </summary>
public abstract class Model(Type type) : Entity<Type>(type)
{
    protected Model() : this(default!) { }
}
