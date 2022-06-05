using HarmonyLib;

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(ClearDialogText), "Start")]
    static class ClearDialog_Start
    {
        static void Prefix(out string __state)
        {
            __state = GameManager.Instance.clubList[1];

            if (MultiplayerManager.MultiplayerFollower)
            {
                GameManager.Instance.clubList[1] = GameManager.Instance.clubList[MultiplayerManager.MyTeam];
            }
        }
        

        static void Postfix(string __state)
        {
            GameManager.Instance.clubList[1] = __state;
        }
    }
}