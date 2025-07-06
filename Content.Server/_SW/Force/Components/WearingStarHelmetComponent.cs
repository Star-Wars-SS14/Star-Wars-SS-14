namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class WearingStarHelmetComponent : Component
{
    //так как на одной сущности может быть несколько предметов, через которые можно говорить по радио, то храним список
    [ViewVariables]
    public List<EntityUid> ObjectsWithStarComponent = new();
}
