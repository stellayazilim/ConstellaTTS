using ConstellaTTS.Domain.Primitives;

namespace ConstellaTTS.Domain;

public class SamplePreProcessedData: Entity<Type>
{
    
    SamplePreProcessedData(Model model): base(model.GetType()) {}
    
    public string DataPath { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return GetType();
        yield return Id;
    }
}