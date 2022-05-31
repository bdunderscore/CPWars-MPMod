using HarmonyLib;
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using CPMod_Multiplayer.HarmonyPatches;
using CPMod_Multiplayer.Serialization;
using UnityEngine;
using UnityModManagerNet;

namespace CPMod_Multiplayer
{
    public class Mod
    {
        static internal AssetBundle assetBundle;
        static internal UnityModManager.ModEntry modEntry;
        static internal UnityModManager.ModEntry.ModLogger logger;

        static internal Harmony harmony;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod.modEntry = modEntry;
            Mod.logger = modEntry.Logger;

            StaticSerializers.RegisterSerializers();

            try
            {
                assetBundle = AssetBundle.LoadFromFile(Path.Combine(modEntry.Path, "CPMod_multiplayer.assetbundle"));
                logger.Log("Asset names in bundle: " + assetBundle.GetAllAssetNames().Join(s => s));
            }
            catch (Exception e)
            {
                LogException("Loading assetbundle", e);
            }

            logger.Log("Applying harmony patches");

            try
            {
                Harmony.DEBUG = true;
                harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                PatchTeamOneChecks.PatchClass(harmony, typeof(Unit));
            } catch (Exception e)
            {
                logger.LogException("Failed to patch game code", e);
                return false;
            }

            modEntry.OnToggle = OnToggle;

            new GameObject("MultiplayerManager").AddComponent<MultiplayerManager>();

            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            try
            {
                MultiplayerManager.MultiplayerSession = value;

                return true;
            } catch (Exception e)
            {
                logger.LogException("Failed to patch/unpatch game code", e);
                return false;
            }
        }

        internal static void LogException(string message, Exception e)
        {
            logger.LogException(message, e);
            logger.Log(e.StackTrace);
            Exception inner = e.InnerException;

            while (inner != null)
            {
                logger.LogException("Caused by", inner);
                logger.Log(inner.StackTrace);
                inner = inner.InnerException;
            }
        }
    }
}
