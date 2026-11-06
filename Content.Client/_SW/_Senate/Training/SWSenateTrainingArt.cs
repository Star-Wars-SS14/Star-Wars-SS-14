using System.Collections.Generic;

namespace Content.Client._SW._Senate.Training;

/// <summary>Every picture the training may draw. Text is never baked into any of them.</summary>
public enum SWTrainingArt
{
    // Existing pictures, shared with the intro, the outro and the statistics console.
    CoruscantSky,
    Rotunda,
    RotundaLights,
    Court,
    DeskBackground,
    DeskWindows,
    DeskForeground,
    DeskLampGlow,
    DeskScreen,
    ChamberAfterSession,
    ArchiveDesk,
    ArchiveLamp,
    SectorTerminal,
    Shuttle,
    PlanetNeutralStone,

    // The 26 pictures of the training contract, delivered separately by the art owner.
    ConsoleCloseupBg,
    HallEntranceBg,
    GearBenchBg,
    ClinicBg,
    LabBg,
    ChartFragment,
    GearGuardHead,
    GearGuardOfficer,
    GearBeltSecurity,
    GearBeltMedical,
    LabVials,
    HandId,
    HandDatapad,
    FigUsher,
    FigGuardHead,
    FigGuardOfficer,
    FigDoctor,
    FigCorrespondent,
    FigChancellor,
    FigSpeaker,
    FigJudge,
    FigSenators,
    FigPatient,
    MarkVeto,
    MarkMutation,
    MarkClock,
}

/// <summary>Whole sheet size in art pixels; a sheet holds <paramref name="Frames"/> equal frames side by side.</summary>
public sealed record SWTrainingArtFile(SWTrainingArt Art, string Path, int Width, int Height, int Frames, bool IsNew);

public enum SWTrainingScene
{
    Coruscant,
    Hall,
    HallSpeaker,
    Court,
    Desk,
    HallEmpty,
    Dossier,
    Terminal,
    Console,
    Entrance,
    Bench,
    Clinic,
    Lab,
}

/// <summary>Background layers, bottom first, and the view centre in the pixels of the first layer.</summary>
public sealed record SWTrainingSceneDef(SWTrainingScene Scene, float CameraX, float CameraY, SWTrainingArt[] Layers);

public static class SWSenateTrainingArt
{
    public const int NewFileCount = 26;

    /// <summary>Frame size of the staging: overlay positions are given in it and its centre is the view centre.</summary>
    public const int FrameWidth = 720;
    public const int FrameHeight = 270;
    /// <summary>The part of the frame that stays visible on every window, however narrow.</summary>
    public const int MinViewWidth = 480;
    public const int ZoneRight = 322;
    public const int ZoneTop = 180;

    public const int SlotAX = 330;
    public const int SlotBX = 440;
    public const int SlotY = 100;

    /// <summary>The vials stand on the lab table. X keeps them clear of the text zone.</summary>
    public const int VialsX = 322;
    public const int VialsY = 105;

    /// <summary>The patient stands beside the doctor in slot A, on the same baseline, so the doctor's hand reaches them.</summary>
    public const int PatientX = 364;
    public const int PatientY = SlotY;

    /// <summary>The doctor's place in the shots with the patient: close enough for the hand to touch them.</summary>
    public const int DoctorWithPatientX = 345;

    /// <summary>Centre of the hand holding the brutepack, inside frame 1 of fig_doctor.</summary>
    public const int DoctorHandX = 88;
    public const int DoctorHandY = 82;

    private const string Root = "/Textures/_SW/_Senate/RoleTraining/";
    private const string Intro = "/Textures/_SW/_Senate/Intro/";
    private const string Outro = "/Textures/_SW/_Senate/Outro/";

    public static readonly SWTrainingArtFile[] Files =
    {
        new(SWTrainingArt.CoruscantSky, Intro + "coruscant_sky.png", 720, 540, 1, false),
        new(SWTrainingArt.Rotunda, Intro + "rotunda.png", 1100, 460, 1, false),
        new(SWTrainingArt.RotundaLights, Intro + "rotunda_lights.png", 1100, 460, 1, false),
        new(SWTrainingArt.Court, Intro + "court.png", 720, 270, 1, false),
        new(SWTrainingArt.DeskBackground, Intro + "desk_background.png", 720, 270, 1, false),
        new(SWTrainingArt.DeskWindows, Intro + "desk_windows.png", 720, 270, 1, false),
        new(SWTrainingArt.DeskForeground, Intro + "desk_foreground.png", 720, 270, 1, false),
        new(SWTrainingArt.DeskLampGlow, Intro + "desk_lamp_glow.png", 720, 270, 1, false),
        new(SWTrainingArt.DeskScreen, Intro + "desk_screen.png", 720, 270, 1, false),
        new(SWTrainingArt.ChamberAfterSession, Outro + "chamber_after_session.png", 960, 360, 1, false),
        new(SWTrainingArt.ArchiveDesk, Outro + "archive_desk.png", 960, 360, 1, false),
        new(SWTrainingArt.ArchiveLamp, Outro + "archive_lamp.png", 960, 360, 1, false),
        new(SWTrainingArt.SectorTerminal, Outro + "sector_terminal.png", 960, 360, 1, false),
        new(SWTrainingArt.Shuttle, Intro + "shuttle.png", 64, 16, 2, false),
        new(SWTrainingArt.PlanetNeutralStone, "/Textures/_SW/Interface/Senate/Statistics/Worlds/planet-neutral-stone-64.png", 64, 64, 1, false),

        new(SWTrainingArt.ConsoleCloseupBg, Root + "console_closeup_bg.png", 720, 270, 1, true),
        new(SWTrainingArt.HallEntranceBg, Root + "hall_entrance_bg.png", 720, 270, 1, true),
        new(SWTrainingArt.GearBenchBg, Root + "gear_bench_bg.png", 720, 270, 1, true),
        new(SWTrainingArt.ClinicBg, Root + "clinic_bg.png", 720, 270, 1, true),
        new(SWTrainingArt.LabBg, Root + "lab_bg.png", 720, 270, 1, true),
        new(SWTrainingArt.ChartFragment, Root + "chart_fragment.png", 840, 152, 2, true),
        new(SWTrainingArt.GearGuardHead, Root + "gear_guard_head.png", 720, 270, 1, true),
        new(SWTrainingArt.GearGuardOfficer, Root + "gear_guard_officer.png", 720, 270, 1, true),
        new(SWTrainingArt.GearBeltSecurity, Root + "gear_belt_security.png", 720, 270, 1, true),
        new(SWTrainingArt.GearBeltMedical, Root + "gear_belt_medical.png", 720, 270, 1, true),
        new(SWTrainingArt.LabVials, Root + "lab_vials.png", 320, 64, 2, true),
        new(SWTrainingArt.HandId, Root + "hand_id.png", 256, 128, 2, true),
        new(SWTrainingArt.HandDatapad, Root + "hand_datapad.png", 256, 128, 2, true),
        new(SWTrainingArt.FigUsher, Root + "fig_usher.png", 192, 160, 2, true),
        new(SWTrainingArt.FigGuardHead, Root + "fig_guard_head.png", 192, 160, 2, true),
        new(SWTrainingArt.FigGuardOfficer, Root + "fig_guard_officer.png", 192, 160, 2, true),
        new(SWTrainingArt.FigDoctor, Root + "fig_doctor.png", 192, 160, 2, true),
        new(SWTrainingArt.FigCorrespondent, Root + "fig_correspondent.png", 192, 160, 2, true),
        new(SWTrainingArt.FigChancellor, Root + "fig_chancellor.png", 192, 160, 2, true),
        new(SWTrainingArt.FigSpeaker, Root + "fig_speaker.png", 192, 160, 2, true),
        new(SWTrainingArt.FigJudge, Root + "fig_judge.png", 192, 160, 2, true),
        new(SWTrainingArt.FigSenators, Root + "fig_senators.png", 320, 160, 2, true),
        new(SWTrainingArt.FigPatient, Root + "fig_patient.png", 320, 160, 2, true),
        new(SWTrainingArt.MarkVeto, Root + "mark_veto.png", 32, 32, 1, true),
        new(SWTrainingArt.MarkMutation, Root + "mark_mutation.png", 32, 32, 1, true),
        new(SWTrainingArt.MarkClock, Root + "mark_clock.png", 32, 32, 1, true),
    };

    public static readonly SWTrainingSceneDef[] Scenes =
    {
        new(SWTrainingScene.Coruscant, 360, 405, new[] { SWTrainingArt.CoruscantSky }),
        new(SWTrainingScene.Hall, 550, 300, new[] { SWTrainingArt.Rotunda, SWTrainingArt.RotundaLights }),
        new(SWTrainingScene.HallSpeaker, 760, 310, new[] { SWTrainingArt.Rotunda, SWTrainingArt.RotundaLights }),
        new(SWTrainingScene.Court, 360, 135, new[] { SWTrainingArt.Court }),
        new(SWTrainingScene.Desk, 360, 135, new[]
        {
            SWTrainingArt.DeskBackground,
            SWTrainingArt.DeskWindows,
            SWTrainingArt.DeskForeground,
            SWTrainingArt.DeskLampGlow,
            SWTrainingArt.DeskScreen,
        }),
        new(SWTrainingScene.HallEmpty, 480, 180, new[] { SWTrainingArt.ChamberAfterSession }),
        new(SWTrainingScene.Dossier, 480, 225, new[] { SWTrainingArt.ArchiveDesk, SWTrainingArt.ArchiveLamp }),
        new(SWTrainingScene.Terminal, 480, 225, new[] { SWTrainingArt.SectorTerminal }),
        new(SWTrainingScene.Console, 360, 135, new[] { SWTrainingArt.ConsoleCloseupBg }),
        new(SWTrainingScene.Entrance, 360, 135, new[] { SWTrainingArt.HallEntranceBg }),
        new(SWTrainingScene.Bench, 360, 135, new[] { SWTrainingArt.GearBenchBg }),
        new(SWTrainingScene.Clinic, 360, 135, new[] { SWTrainingArt.ClinicBg }),
        new(SWTrainingScene.Lab, 360, 135, new[] { SWTrainingArt.LabBg }),
    };

    private static readonly Dictionary<SWTrainingArt, SWTrainingArtFile> ByArt = BuildByArt();
    private static readonly Dictionary<SWTrainingScene, SWTrainingSceneDef> ByScene = BuildByScene();

    public static SWTrainingArtFile File(SWTrainingArt art) => ByArt[art];

    public static SWTrainingSceneDef Scene(SWTrainingScene scene) => ByScene[scene];

    private static Dictionary<SWTrainingArt, SWTrainingArtFile> BuildByArt()
    {
        var result = new Dictionary<SWTrainingArt, SWTrainingArtFile>();
        foreach (var file in Files)
            result[file.Art] = file;
        return result;
    }

    private static Dictionary<SWTrainingScene, SWTrainingSceneDef> BuildByScene()
    {
        var result = new Dictionary<SWTrainingScene, SWTrainingSceneDef>();
        foreach (var scene in Scenes)
            result[scene.Scene] = scene;
        return result;
    }
}
