using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public sealed class SWHealthCareView
{
    public int TeamLimit = 3;
    public int BusyTeams;
    public int OrderLimit = 6;
    public int OpenOrders;
    public int CompletedCampaigns;
    public double CompletedCourses;
    public Dictionary<string, float> SectorHealthcareEffects = new();
    public List<SWHealthCareNeedView> Needs = new();
    public List<SWHealthCareOrderView> Orders = new();
}

[Serializable, NetSerializable]
public sealed class SWHealthCareNeedView
{
    public string WorldId = "";
    public bool Prosthetic;
    public double WaitingCourses;
    public double ReservedCourses;
    public double SessionCourses;
    public double WorkHours;
    public int Cost;
    public bool CanStart;
    public string UnavailableReason = "";
    public List<SWHealthMaterialView> Materials = new();
}

[Serializable, NetSerializable]
public sealed class SWHealthCareOrderView
{
    public string Id = "";
    public string WorldId = "";
    public bool Prosthetic;
    public double Courses;
    public double CompletedCourses;
    public float Progress;
    public double HoursLeft;
    public bool Completed;
    public bool Working;
    public string Status = "";
    public List<SWHealthMaterialView> Materials = new();
}
