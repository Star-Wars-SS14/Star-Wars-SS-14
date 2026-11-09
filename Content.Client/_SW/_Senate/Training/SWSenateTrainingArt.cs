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

    // Pictures of the staged chapters, delivered separately by the art owner.
    FigDoctorConsole,
    ShiftConsole,
    ShiftConsoleBg,
    UiPointer,
    GearBeltMedicalClosed,
    FigDoctorTreat,
    ConsoleCloseupMounted,
    FigSpeakerConsole,
    WorkConsole,

    // Action poses for single stages; the plain figures stay in every other scene. Frame 0 stands, frame 1 acts.
    /// <summary>The guard head in slot A pointing right, towards slot B.</summary>
    FigGuardHeadPoint,
    /// <summary>The guard officer in slot A with a hand at the headset.</summary>
    FigGuardOfficerRadio,
    /// <summary>The usher in slot B with the staff, the open palm pointing left, towards slot A.</summary>
    FigUsherGuide,
    /// <summary>The doctor at the treatment position looking the patient over, without touching.</summary>
    FigDoctorExamine,

    // Single items of the usher's filled security belt, each shown large while it is explained.
    /// <summary>The game's stun baton, enlarged.</summary>
    GearItemStunbaton,
    /// <summary>The game's handcuffs, enlarged.</summary>
    GearItemHandcuffs,
    /// <summary>The first frame of the game's security holoprojector, enlarged.</summary>
    GearItemHoloprojector,
    /// <summary>The game's handheld security radio, enlarged.</summary>
    GearItemRadio,
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
    /// <summary>The console with the card reader on its post, for every shot where a hand uses it.</summary>
    ConsoleMounted,
    Entrance,
    Bench,
    Clinic,
    Lab,
    ShiftRoom,
}

/// <summary>Background layers, bottom first, and the view centre in the pixels of the first layer.</summary>
public sealed record SWTrainingSceneDef(SWTrainingScene Scene, float CameraX, float CameraY, SWTrainingArt[] Layers);

public static class SWSenateTrainingArt
{
    public const int NewFileCount = 43;

    /// <summary>
    /// Where a single belt item is shown: inside the part of the frame every window keeps (x 120 to 599), above the text
    /// zone and left of the remarks, on a light plate. The plate reaches <see cref="BackingPad"/> pixels around the item:
    /// from x 130 to 238, inside the kept part and clear of the belt's edge.
    /// </summary>
    public const int BeltItemX = 136;
    public const int BeltItemY = 40;
    public const int BeltItemSize = 96;

    /// <summary>Art pixels of the neutral plate around a backed picture.</summary>
    public const int BackingPad = 6;

    /// <summary>The checked treatment layer goes over the patient; its frames are wider than the standing doctor's.</summary>
    public const int TreatDoctorX = 305;

    /// <summary>The examining doctor shares the treatment position, so the patient keeps one place in every shot.</summary>
    public const int ExamineDoctorX = 305;

    /// <summary>The doctor and the shift console share the frame's origin; the art owner confirms this composition.</summary>
    public const int ShiftDoctorX = 330;
    public const int ShiftDoctorY = 100;
    public const int ShiftConsoleX = 375;
    public const int ShiftConsoleY = 110;

    /// <summary>A figure at the work console; the two sheets are drawn together, so the hand meets the console.</summary>
    public const int WorkFigureX = 330;
    public const int WorkFigureY = 100;
    public const int WorkConsoleX = 375;
    public const int WorkConsoleY = 110;

    /// <summary>The console screen in the frame. Illustrations of game windows are placed inside it.</summary>
    public const int ScreenLeft = 150;
    public const int ScreenTop = 20;
    public const int ScreenRight = 570;
    public const int ScreenBottom = 172;

    /// <summary>In the shift room the illustration stands left of the doctor, so the console stays in the picture.</summary>
    public const int RoomPanelLeft = 44;
    public const int RoomPanelRight = 322;

    /// <summary>The part of the frame given to an illustration; it always ends above the text zone.</summary>
    public static (int Left, int Top, int Right, int Bottom) PanelBox(SWTrainingScene scene) => scene == SWTrainingScene.ShiftRoom
        ? (RoomPanelLeft, ScreenTop, RoomPanelRight, ScreenBottom)
        : (ScreenLeft, ScreenTop, ScreenRight, ScreenBottom);

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

    /// <summary>The standing doctor alone, roughly where the treatment layer's figure stands when the patient appears.</summary>
    public const int DoctorAloneX = 312;

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
        new(SWTrainingArt.FigDoctorConsole, Root + "fig_doctor_console.png", 192, 160, 2, true),
        new(SWTrainingArt.ShiftConsole, Root + "shift_console.png", 256, 160, 2, true),
        new(SWTrainingArt.ShiftConsoleBg, Root + "shift_console_bg.png", 720, 270, 1, true),
        new(SWTrainingArt.UiPointer, Root + "ui_pointer.png", 48, 24, 2, true),
        new(SWTrainingArt.GearBeltMedicalClosed, Root + "gear_belt_medical_closed.png", 720, 270, 1, true),
        new(SWTrainingArt.FigDoctorTreat, Root + "fig_doctor_treat.png", 512, 160, 4, true),
        new(SWTrainingArt.ConsoleCloseupMounted, Root + "console_closeup_mounted.png", 720, 270, 1, true),
        new(SWTrainingArt.FigSpeakerConsole, Root + "fig_speaker_console.png", 192, 160, 2, true),
        new(SWTrainingArt.WorkConsole, Root + "work_console.png", 256, 160, 2, true),
        new(SWTrainingArt.FigGuardHeadPoint, Root + "fig_guard_head_point.png", 192, 160, 2, true),
        new(SWTrainingArt.FigGuardOfficerRadio, Root + "fig_guard_officer_radio.png", 192, 160, 2, true),
        new(SWTrainingArt.FigUsherGuide, Root + "fig_usher_guide.png", 192, 160, 2, true),
        new(SWTrainingArt.FigDoctorExamine, Root + "fig_doctor_examine.png", 192, 160, 2, true),

        // 96×96 enlargements of the game's own item sprites, delivered for the training.
        new(SWTrainingArt.GearItemStunbaton, Root + "gear_detail_stunbaton.png", 96, 96, 1, true),
        new(SWTrainingArt.GearItemHandcuffs, Root + "gear_detail_handcuffs.png", 96, 96, 1, true),
        new(SWTrainingArt.GearItemHoloprojector, Root + "gear_detail_holoprojector.png", 96, 96, 1, true),
        new(SWTrainingArt.GearItemRadio, Root + "gear_detail_radio.png", 96, 96, 1, true),
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
        new(SWTrainingScene.ConsoleMounted, 360, 135, new[] { SWTrainingArt.ConsoleCloseupMounted }),
        new(SWTrainingScene.Entrance, 360, 135, new[] { SWTrainingArt.HallEntranceBg }),
        new(SWTrainingScene.Bench, 360, 135, new[] { SWTrainingArt.GearBenchBg }),
        new(SWTrainingScene.Clinic, 360, 135, new[] { SWTrainingArt.ClinicBg }),
        new(SWTrainingScene.Lab, 360, 135, new[] { SWTrainingArt.LabBg }),
        new(SWTrainingScene.ShiftRoom, 360, 135, new[] { SWTrainingArt.ShiftConsoleBg }),
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
