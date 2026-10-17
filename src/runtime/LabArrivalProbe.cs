using System.Text.Json;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of the camera framing when a run arrives in a level (#126).
/// A new run starts directly in <c>KIRBY_LAB_ARRIVAL_LEVEL</c> (default
/// <c>Ossuary</c>) and Kirby stands still. Every host frame records the hero's
/// host position, the viewport's centre, size and zoom, the host's awake and
/// cinematic state and the source camera, so the frames where the view is far
/// from Kirby, and what moved it, can be read from <c>arrival-trace.jsonl</c>.
/// Each line also has the host head's state (#189): whether the hero has a head,
/// its mode, whether its eye shows and whether any of its particles are alive.
/// </summary>
internal static class LabArrivalProbe
{
    private static string? run;
    private static int frames;
    private static readonly JsonSerializerOptions TraceOptions = new()
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    internal static void Install(string output)
    {
        run = output;
        string level = Environment.GetEnvironmentVariable("KIRBY_LAB_ARRIVAL_LEVEL") ?? "Ossuary";
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.Hook_User.newGame += (orig, user, seed, info, custom, scoring, mode) =>
            orig(user, seed, ((HaxeProxyBase)dc.Data.Class.level.resolve(level.AsHaxeString(), false)).ToVirtual<LevelInfo>(),
                custom, scoring, mode);
        KirbyInput.TestOverride = _ => 0;
        // The values the host's camera reads when it updates, which are not the
        // ones left at the end of the frame.
        dc.Hook_Viewport.update += (orig, viewport) =>
        {
            if (viewport.tracked is dc.en.Hero hero)
                atCamera = new { hero.dx, hero.dy, hero.bdx, hero.bdy, hero.cx, hero.cy, hero.xr, hero.yr,
                    hero.ignoreSlopes, mode = hero.collisionMode?.ToString(), viewport.vertical, viewport.horizontal,
                    diveAffects = (hero.affects.getDyn(11) as dc.hl.types.ArrayObj)?.length, dives = Dives(hero) };
            orig(viewport);
        };
    }

    private static object? atCamera;

    private static string[] Dives(dc.en.Hero hero)
    {
        var found = new List<string>();
        var skills = hero.mainSkillsManager?.skills;
        for (int i = 0; skills is not null && i < skills.length; i++)
            if (skills.getDyn(i) is dc.tool.mainSkills.DiveAttack dive)
                found.Add($"{dive.GetType().Name} active={dive.isActive()} enabled={dive.isEnabled()}");
        return [.. found];
    }

    /// <summary>Each host frame once a hero exists.</summary>
    internal static void Poll()
    {
        if (run is null) return;
        var hero = ModCore.Modules.Game.Instance.HeroInstance;
        if (hero is null || hero.destroyed || hero._level is null) return;
        var level = hero._level;
        var viewport = level.viewport;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        double heroX = (hero.cx + hero.xr) * 24, heroY = (hero.cy + hero.yr) * 24;
        var head = hero.heroHead;
        File.AppendAllText(Path.Combine(run, "arrival-trace.jsonl"), JsonSerializer.Serialize(new
        {
            host = frames++, level = level.uniqId, kirby = KirbyMod.Instance?.ActiveSession is not null,
            hero.awake, cinematic = level.game.hasCinematic(), heroX, heroY, hero.dx, hero.dy,
            vpX = viewport?.realX, vpY = viewport?.realY, vpWid = viewport?.wid, vpHei = viewport?.hei, zoom = viewport?.zoom,
            offsetX = viewport is null ? (double?)null : heroX - viewport.realX,
            offsetY = viewport is null ? (double?)null : heroY - viewport.realY,
            atCamera,
            head = head is null ? null : new { head.heroHasHead, mode = head.get_headMode()?.ToString(), eye = head.eye?.visible,
                particles = head.pool is { } pool && !(pool.busyList_Killable.isEmpty() && pool.busyList_DontKillEarly.isEmpty()) },
            source = machine is null ? null : new { frame = machine.State.Frame, x = machine.State.X, y = machine.State.Y,
                cameraX = machine.State.CameraX, cameraY = machine.State.CameraY },
        }, TraceOptions) + "\n");
    }
}
