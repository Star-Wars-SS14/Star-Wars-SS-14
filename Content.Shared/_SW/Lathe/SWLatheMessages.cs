using Robust.Shared.Serialization;

namespace Content.Shared._SW.Lathe;

[NetSerializable, Serializable]
public enum SWLatheUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWLatheBoundUserInterfaceState : BoundUserInterfaceState
{
    public List<SWLatheRecipeInfo> Recipes;
    public Dictionary<string, int> StashedMaterials;
    public int MaxMaterialAmount;
    public bool ProductionDisabled;
    public string? SelectedRecipe;
    public string? CurrentRecipe;
    public TimeSpan? ProductionStartTime;
    public TimeSpan? ProductionEndTime;

    public SWLatheBoundUserInterfaceState(
        List<SWLatheRecipeInfo> recipes,
        Dictionary<string, int> stashedMaterials,
        int maxMaterialAmount,
        bool productionDisabled,
        string? selectedRecipe,
        string? currentRecipe,
        TimeSpan? productionStartTime,
        TimeSpan? productionEndTime)
    {
        Recipes = recipes;
        StashedMaterials = stashedMaterials;
        MaxMaterialAmount = maxMaterialAmount;
        ProductionDisabled = productionDisabled;
        SelectedRecipe = selectedRecipe;
        CurrentRecipe = currentRecipe;
        ProductionStartTime = productionStartTime;
        ProductionEndTime = productionEndTime;
    }
}

[Serializable, NetSerializable]
public sealed class SWLatheRecipeInfo
{
    public string Id;
    public Dictionary<string, int> Materials;
    public Dictionary<string, int> Result;
    public float ProduceTime;

    public SWLatheRecipeInfo(
        string id,
        Dictionary<string, int> materials,
        Dictionary<string, int> result,
        float produceTime)
    {
        Id = id;
        Materials = materials;
        Result = result;
        ProduceTime = produceTime;
    }
}

[Serializable, NetSerializable]
public sealed class SWLatheSelectRecipeMessage : BoundUserInterfaceMessage
{
    public string? Recipe;
    public bool ProductionDisabled;

    public SWLatheSelectRecipeMessage(string? recipe, bool productionDisabled = false)
    {
        Recipe = recipe;
        ProductionDisabled = productionDisabled;
    }
}
