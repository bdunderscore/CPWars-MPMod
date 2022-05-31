using CPMod_Multiplayer.Serialization;
using HarmonyLib;
using UnityEngine;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(LogManager), nameof(LogManager.CreateMessage))]
    class LogManager_CreateMessage
    {
        static void Prefix(
            string club,
            Color color,
            string name,
            string displayName,
            string text,
            int newLv,
            string name2
        )
        {
            PuppetMaster.EnqueueAdhocPacket(new NetLogCreateMessage()
            {
                club = club,
                color = new NetColor()
                {
                    r = color.r,
                    g = color.g,
                    b = color.b,
                    a = color.a
                },
                name = name,
                displayName = displayName,
                text = text,
                newLv = newLv,
                name2 = name2
            });
        }
    }

    [HarmonyPatch(typeof(LogManager), nameof(LogManager.CreateGetMessage))]
    class LogManager_CreateGetMessage
    {
        static void Prefix(string name, string displayName, string text)
        {
            PuppetMaster.EnqueueAdhocPacket(new NetLogCreateGetMessage()
            {
                name = name,
                displayName = displayName,
                text = text
            });
        }
    }
}