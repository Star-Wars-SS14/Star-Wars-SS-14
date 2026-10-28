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
            handle.DrawRect(UIBox2.FromDimensions(p * UIScale, new Vector2(tile * UIScale)), new Color(0.05f, .1f, .14f, .08f), false);
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
        // Buildings sit beside the road centreline. The short driveway connects the
        // entrance to the actual network cell and leaves the traffic lane unobstructed.
        var wall = match ? Ink("#75888A") : Ink("#475955");
        var roof = match ? Ink("#314952") : Ink("#293A38");
        var light = match ? Ink("#C7D1C3") : Ink("#60716A");
        var accent = match ? Ink("#D2B27A") : Ink("#66726A");
        void Line(float ax, float ay, float bx, float by, Color? tint = null) => h.DrawLine(
            (p + new Vector2(ax, ay) * t) * UIScale, (p + new Vector2(bx, by) * t) * UIScale, tint ?? light);
        void Box(float x, float y, float w, float height, Color color, bool filled = true) => h.DrawRect(
            UIBox2.FromDimensions((p + new Vector2(x, y) * t) * UIScale, new Vector2(w, height) * t * UIScale), color, filled);
        void Circle(float x, float y, float radius, Color color) => h.DrawCircle(
            (p + new Vector2(x, y) * t) * UIScale, radius * t * UIScale, Color.ToSrgb(color), false);
        void Hall(float x, float width)
        {
            Box(x, .12f, width, .22f, wall);
            Box(x + .025f, .14f, width - .05f, .11f, roof);
            Line(x, .34f, x + width, .34f, light);
        }
        var symbol = site.Kind;
        if (symbol == "production" && site.Recipes.Count > 0 && _recipes.TryGetValue(site.Recipes[0].Id, out var recipe))
            symbol = recipe.Facility.Id switch
            {
                "farm" or "bioreactor" => "agriculture", "mine" or "quarry" => "extraction",
                "pharma" or "medtech" => "medical", "shipyard" => "shipyard",
                "waterworks" or "fishery" => "water", _ => "production",
            };
        if (site.Resource?.Id == "water_source") symbol = "water";
        if (site.Resource?.Id is "arable" or "medical_biome") symbol = "agriculture";
        switch (symbol)
        {
            case "agriculture":
                Hall(.12f, .22f);
                for (var i = 0; i < 3; i++) Line(.43f + i * .14f, .08f, .43f + i * .14f, .34f, accent);
                Line(.39f, .34f, .85f, .34f, wall);
                break;
            case "extraction":
            case "resource":
                Hall(.12f, .22f);
                Line(.48f, .34f, .65f, .06f, accent); Line(.65f, .06f, .83f, .34f, accent);
                Line(.52f, .27f, .79f, .27f, wall); Line(.65f, .08f, .65f, .35f, light);
                break;
            case "shipyard":
                Hall(.10f, .20f);
                Line(.39f, .08f, .39f, .36f, accent); Line(.86f, .08f, .86f, .36f, accent);
                Line(.39f, .08f, .86f, .08f, accent);
                Line(.48f, .3f, .62f, .12f, light); Line(.62f, .12f, .78f, .3f, light); Line(.48f, .3f, .78f, .3f, light);
                break;
            case "medical":
                Hall(.18f, .64f);
                Line(.5f, .15f, .5f, .27f, accent); Line(.43f, .21f, .57f, .21f, accent);
                break;
            case "water":
                Hall(.12f, .27f); Circle(.65f, .21f, .14f, accent); Line(.52f, .21f, .78f, .21f, light);
                break;
            case "port":
                Hall(.08f, .25f); Circle(.62f, .22f, .16f, accent);
                Line(.48f, .22f, .76f, .22f, light); Line(.62f, .09f, .62f, .35f, light);
                break;
            case "power":
                Hall(.08f, .23f); Circle(.63f, .21f, .15f, accent);
                Line(.65f, .1f, .57f, .22f, light); Line(.57f, .22f, .68f, .22f, light); Line(.68f, .22f, .61f, .32f, light);
                break;
            case "storage":
                Hall(.09f, .38f); Hall(.54f, .36f);
                Line(.17f, .28f, .38f, .28f, accent); Line(.62f, .28f, .82f, .28f, accent);
                break;
            case "settlement":
                if (site.Id == _settlementCenter)
                {
                    Hall(.18f, .64f); Line(.14f, .13f, .5f, .025f, accent); Line(.5f, .025f, .86f, .13f, accent);
                    for (var i = 0; i < 3; i++) Line(.32f + i * .18f, .23f, .32f + i * .18f, .34f, light);
                }
                else
                {
                    Hall(.12f, .19f); Hall(.39f, .19f); Hall(.66f, .19f);
                    Line(.12f, .12f, .215f, .055f, accent); Line(.215f, .055f, .31f, .12f, accent);
                    Line(.66f, .12f, .755f, .055f, accent); Line(.755f, .055f, .85f, .12f, accent);
                }
                break;
            default:
                Hall(.09f, .56f); Hall(.69f, .21f);
                for (var i = 0; i < 3; i++)
                {
                    var x = .12f + i * .17f;
                    Line(x, .12f, x + .085f, .05f, accent); Line(x + .085f, .05f, x + .16f, .12f, accent);
                }
                Box(.73f, .03f, .06f, .1f, wall);
                break;
        }
        // A small loading court and doorway are aligned with the road passing through the cell.
        Line(.34f, .38f, .66f, .38f, wall);
        Line(.5f, .34f, .5f, .43f, accent);
        if (site.Kind == "production")
        {
            var running = site.Recipes.Any(r => _production.TryGetValue(r.Id, out var line)
                && (line.WorkInProgress > 1e-9 || line.CompletedCyclesPerHour > 1e-9));
            var status = !_ready || _paused ? Ink("#839093") : running ? Ink("#92C8A7") : Ink("#D6B577");
            Box(.84f, .03f, .07f, .04f, match ? status : Ink("#566464"));
        }
    }
}
