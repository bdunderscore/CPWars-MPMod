using HarmonyLib;

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(TutorialDataRepository), nameof(TutorialDataRepository.GetFromEventName))]
    class TutorialData_GetFromEventName
    {
        static bool Prefix(ref TutorialDataRepository.Tutorial __result)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                __result = new TutorialDataRepository.Tutorial()
                {
                    viewed = true,
                    tutorialName = "---multiplayer_disabled---"
                };
                return false;
            }

            return true;
        }
    }
}