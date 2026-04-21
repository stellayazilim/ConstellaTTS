using ConstellaTTS.Domain;
using ConstellaTTS.Domain.Primitives;
using ConstellaTTS.SDK;

namespace ConstellaTTS.Core.ViewModels;

public partial class SectionViewModel<TModel>(Section<TModel> section)
    : SectionViewModel(section)
    where TModel : Model
{
    public TModel? Model { get; } = section.Model;
}
