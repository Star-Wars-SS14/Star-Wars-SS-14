using System;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed partial class SWPlanetSurfaceControl
{
    private static Color Ink(string hex) => Color.FromHex(hex);
    private static Color TerrainColor(char terrain) => terrain switch
    {
        'c' => Ink("#304554"), 'f' => Ink("#24483E"), 'm' => Ink("#57565A"), 'w' => Ink("#1B4054"),
        'd' => Ink("#6C5840"), 'i' => Ink("#63808D"), 's' => Ink("#354C42"), 'r' => Ink("#514D45"),
        'v' => Ink("#483635"), 'o' => Ink("#14202E"), _ => Ink("#465746"),
    };

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWConsoleStyle.Navigation);
        var tile = TileSize;
        var origin = Origin;
        for (var y = 0; y < Surface.Height; y++)
        for (var x = 0; x < Surface.Width; x++)
        {
            var cell = new Vector2i(x, y);
            var p = origin + new Vector2(x, y) * tile;
            if (p.X > Size.X || p.Y > Size.Y || p.X + tile < 0 || p.Y + tile < 0) continue;
            var terrain = Surface.Terrain[y][x];
            var color = TerrainColor(terrain);
            var light = .96f + ((x * 17 + y * 11) % 7) * .012f;
            color = new Color(color.R * light, color.G * light, color.B * light, 1);
            handle.DrawRect(UIBox2.FromDimensions(p * UIScale, new Vector2(tile * UIScale + .5f)), color);
            TerrainGlyph(handle, p, tile, terrain, x, y);
            handle.DrawRect(UIBox2.FromDimensions(p * UIScale, new Vector2(tile * UIScale)), new Color(0.05f, .1f, .14f, .25f), false);
        }

        DrawRoads(handle, tile);
        DrawTraffic(handle, tile);

        foreach (var site in Surface.Sites)
        {
            var center = CellToLocal(site.Cell);
            if (center.X + tile < 0 || center.Y + tile < 0 || center.X - tile > Width || center.Y - tile > Height) continue;
            Building(handle, site, center - new Vector2(tile * .5f), tile, Matches(site));
        }

        if (_hover is { } hover && hover != SelectedCell) CellFrame(handle, hover, tile, SWConsoleStyle.TextDim, false);
        if (SelectedCell is { } selected) CellFrame(handle, selected, tile, SWConsoleStyle.Brass, true);
        var bounds = UIBox2.FromDimensions(origin * UIScale, new Vector2(Surface.Width, Surface.Height) * tile * UIScale);
        handle.DrawRect(bounds, SWConsoleStyle.Edge, false);
        if (Zoom <= 1.001f)
        {
            for (var x = 0; x < Surface.Width; x++)
                handle.DrawString(SWConsoleStyle.Small, (origin + new Vector2((x + .5f) * tile - 4, -17)) * UIScale,
                    ((char) ('A' + x)).ToString(), UIScale, SWConsoleStyle.TextDim);
            for (var y = 0; y < Surface.Height; y++)
                handle.DrawString(SWConsoleStyle.Small, (origin + new Vector2(-23, (y + .5f) * tile - 7)) * UIScale,
                    (y + 1).ToString(), UIScale, SWConsoleStyle.TextDim);
        }
    }

    private void CellFrame(DrawingHandleScreen h, Vector2i cell, float tile, Color color, bool selected)
    {
        var p = CellToLocal(cell) - new Vector2(tile / 2);
        var box = UIBox2.FromDimensions((p + new Vector2(1)) * UIScale, new Vector2(tile - 2) * UIScale);
        h.DrawRect(box, color, false);
        if (!selected) return;
        h.DrawRect(UIBox2.FromDimensions((p + new Vector2(2, tile - 4)) * UIScale, new Vector2(tile - 4, 3) * UIScale), color);
    }

    private void TerrainGlyph(DrawingHandleScreen h, Vector2 p, float t, char terrain, int x, int y)
    {
        var detail = new Color(.65f, .74f, .7f, .18f);
        void Line(float ax, float ay, float bx, float by, Color? tint = null) =>
            h.DrawLine((p + new Vector2(ax, ay) * t) * UIScale, (p + new Vector2(bx, by) * t) * UIScale, tint ?? detail);
        void Box(float ax, float ay, float w, float height, Color tint) =>
            h.DrawRect(UIBox2.FromDimensions((p + new Vector2(ax, ay) * t) * UIScale, new Vector2(w, height) * t * UIScale), tint);
        switch (terrain)
        {
            case 'c':
                Box(.12f, .12f, .28f, .32f, Ink("#405A69"));
                Box(.56f, .12f, .3f, .22f, Ink("#3C515E"));
                Box(.12f, .62f, .28f, .24f, Ink("#293B49"));
                Box(.55f, .5f, .31f, .36f, Ink("#3D5262"));
                Line(0, .52f, 1, .52f, new Color(.71f, .78f, .77f, .23f));
                Line(.47f, 0, .47f, 1);
                break;
            case 'f':
                for (var i = 0; i < 3; i++)
                {
                    var cx = .22f + i * .27f;
                    var cy = .28f + ((i + x + y) % 2) * .25f;
                    Line(cx - .13f, cy + .2f, cx, cy - .08f, Ink("#3C6552"));
                    Line(cx, cy - .08f, cx + .13f, cy + .2f, Ink("#52775D"));
                    Line(cx - .13f, cy + .2f, cx + .13f, cy + .2f, Ink("#32553F"));
                }
                break;
            case 'w':
                for (var i = 0; i < 3; i++)
                {
                    var yy = .25f + i * .24f;
                    Line(.14f, yy, .43f, yy + .05f, new Color(.39f, .66f, .74f, .3f));
                    Line(.43f, yy + .05f, .8f, yy, new Color(.39f, .66f, .74f, .3f));
                }
                break;
            case 'm':
            case 'r':
                Line(.1f, .78f, .4f, .25f); Line(.4f, .25f, .7f, .78f);
                Line(.48f, .64f, .69f, .37f); Line(.69f, .37f, .9f, .78f);
                Line(.4f, .25f, .4f, .58f);
                break;
            case 'o':
                Line(.08f, .5f, .92f, .5f, Ink("#30495D"));
                h.DrawCircle((p + new Vector2(.5f) * t) * UIScale, t * .33f * UIScale, Color.ToSrgb(Ink("#273C51")), false);
                break;
            case 'i':
                Line(.1f, .65f, .35f, .3f); Line(.35f, .3f, .8f, .4f); Line(.5f, .6f, .85f, .7f);
                break;
            case 'v':
                Line(.1f, 0, .4f, .35f, Ink("#9C5C3D")); Line(.4f, .35f, .3f, .7f, Ink("#9C5C3D"));
                Line(.3f, .7f, .6f, 1, Ink("#AC6B42"));
                break;
            default:
                Line(.12f, .38f, .42f, .28f); Line(.42f, .28f, .75f, .32f);
                Line(.25f, .75f, .56f, .65f); Line(.56f, .65f, .88f, .71f);
                break;
        }
    }

    private void Building(DrawingHandleScreen h, SWPlanetSurfaceSite site, Vector2 p, float t, bool match)
    {
        var ink = match ? Ink("#D3DAD8") : Ink("#66746F");
        var accent = site.Kind switch
        {
            "resource" => Ink("#D7BC83"), "port" => Ink("#9BD3E0"), "power" => Ink("#E3B96F"),
            "settlement" => Ink("#BED0BD"), _ => Ink("#C5CDCD"),
        };
        if (!match) accent = Ink("#66746F");
        void Line(float ax, float ay, float bx, float by, Color? tint = null) => h.DrawLine(
            (p + new Vector2(ax, ay) * t) * UIScale, (p + new Vector2(bx, by) * t) * UIScale, tint ?? ink);
        void Box(float ax, float ay, float w, float height, Color tint, bool filled = true) => h.DrawRect(
            UIBox2.FromDimensions((p + new Vector2(ax, ay) * t) * UIScale, new Vector2(w, height) * t * UIScale), tint, filled);
        Box(.1f, .14f, .82f, .77f, new Color(.025f, .045f, .06f, .55f));
        Box(.08f, .1f, .82f, .76f, Ink("#182934"));
        Box(.08f, .1f, .82f, .76f, match ? Ink("#607477") : Ink("#3E4D50"), false);
        var symbol = site.Kind;
        if (symbol == "production" && site.Recipes.Count > 0)
            symbol = _recipes[site.Recipes[0].Id].Facility.Id switch
            {
                "farm" or "bioreactor" => "agriculture",
                "mine" or "quarry" => "extraction",
                "pharma" or "medtech" => "medical",
                "shipyard" => "shipyard",
                "waterworks" or "fishery" => "water",
                _ => "production",
            };
        if (site.Resource?.Id == "water_source") symbol = "water";
        if (site.Resource?.Id is "arable" or "medical_biome") symbol = "agriculture";
        switch (symbol)
        {
            case "agriculture":
                Line(.5f, .27f, .5f, .72f, accent);
                Line(.5f, .42f, .28f, .28f, accent); Line(.28f, .28f, .29f, .43f, accent); Line(.29f, .43f, .5f, .54f, accent);
                Line(.5f, .51f, .72f, .34f, accent); Line(.72f, .34f, .7f, .53f, accent); Line(.7f, .53f, .5f, .65f, accent);
                Line(.25f, .74f, .75f, .74f);
                break;
            case "extraction":
                Line(.24f, .72f, .49f, .24f); Line(.49f, .24f, .75f, .72f);
                Line(.3f, .59f, .68f, .59f); Line(.37f, .44f, .6f, .44f);
                Line(.49f, .24f, .49f, .64f, accent); Box(.4f, .64f, .18f, .11f, accent);
                break;
            case "medical":
                Box(.36f, .24f, .26f, .5f, accent); Box(.24f, .36f, .5f, .26f, accent);
                break;
            case "shipyard":
                Line(.18f, .24f, .18f, .73f); Line(.8f, .24f, .8f, .73f);
                Line(.18f, .24f, .32f, .24f); Line(.8f, .24f, .66f, .24f);
                Line(.49f, .22f, .67f, .63f, accent); Line(.67f, .63f, .31f, .63f, accent);
                Line(.31f, .63f, .49f, .22f, accent); Box(.42f, .64f, .14f, .11f, accent);
                break;
            case "water":
                Line(.49f, .23f, .29f, .53f, accent); Line(.49f, .23f, .69f, .53f, accent);
                Line(.29f, .53f, .34f, .66f, accent); Line(.69f, .53f, .64f, .66f, accent);
                Line(.34f, .66f, .64f, .66f, accent); Line(.27f, .76f, .71f, .76f);
                break;
            case "production":
                Box(.22f, .47f, .53f, .25f, ink, false);
                Line(.22f, .47f, .39f, .34f); Line(.39f, .34f, .39f, .47f);
                Line(.39f, .47f, .55f, .34f); Line(.55f, .34f, .55f, .47f);
                Box(.64f, .26f, .09f, .23f, ink);
                for (var i = 0; i < 3; i++) Box(.29f + i * .16f, .56f, .07f, .07f, accent);
                break;
            case "port":
                h.DrawCircle((p + new Vector2(.49f, .47f) * t) * UIScale, t * .23f * UIScale, Color.ToSrgb(accent), false);
                Line(.36f, .35f, .36f, .59f); Line(.62f, .35f, .62f, .59f); Line(.36f, .47f, .62f, .47f);
                Line(.2f, .73f, .78f, .73f, accent);
                break;
            case "resource":
                Line(.23f, .57f, .4f, .29f, accent); Line(.4f, .29f, .57f, .57f, accent);
                Line(.23f, .57f, .43f, .71f, accent); Line(.43f, .71f, .57f, .57f, accent);
                Line(.55f, .4f, .68f, .3f, accent); Line(.68f, .3f, .8f, .6f, accent);
                Line(.8f, .6f, .6f, .72f, accent);
                break;
            case "power":
                h.DrawCircle((p + new Vector2(.49f, .48f) * t) * UIScale, t * .23f * UIScale, Color.ToSrgb(accent), false);
                Line(.54f, .25f, .39f, .49f, accent); Line(.39f, .49f, .59f, .49f, accent); Line(.59f, .49f, .44f, .72f, accent);
                break;
            case "storage":
                Box(.21f, .46f, .24f, .23f, ink, false); Box(.5f, .46f, .24f, .23f, ink, false);
                Box(.36f, .22f, .24f, .23f, ink, false); Line(.24f, .73f, .76f, .73f);
                break;
            case "settlement" when site.Id == _settlementCenter:
                // Civic centre: a broad hall, stepped roof and a small public square.
                Box(.23f, .43f, .53f, .27f, ink, false);
                Line(.18f, .43f, .49f, .24f, accent); Line(.49f, .24f, .8f, .43f, accent);
                for (var i = 0; i < 3; i++) Line(.32f + i * .17f, .47f, .32f + i * .17f, .67f);
                Box(.2f, .74f, .59f, .05f, accent);
                break;
            case "settlement":
                // Residential districts: separate low roofs instead of the civic landmark.
                for (var i = 0; i < 3; i++)
                {
                    var xx = .18f + i * .22f;
                    var yy = i == 1 ? .32f : .47f;
                    Box(xx, yy, .17f, .23f, ink, false);
                    Line(xx - .02f, yy, xx + .085f, yy - .1f, accent);
                    Line(xx + .085f, yy - .1f, xx + .19f, yy, accent);
                }
                Line(.18f, .76f, .81f, .76f);
                break;
            default:
                Box(.21f, .42f, .15f, .3f, ink, false); Box(.41f, .25f, .17f, .47f, ink, false);
                Box(.63f, .36f, .14f, .36f, ink, false); Line(.18f, .75f, .8f, .75f);
                break;
        }
        if (site.Kind != "production") return;
        var running = site.Recipes.Any(r => _production.TryGetValue(r.Id, out var line)
            && (line.WorkInProgress > 1e-9 || line.CompletedCyclesPerHour > 1e-9));
        var stateColor = !_ready || _paused ? Ink("#839093") : running ? Ink("#92C8A7") : Ink("#D6B577");
        if (!match) stateColor = Ink("#566464");
        Box(.65f, .12f, .2f, .06f, stateColor);
    }
}
