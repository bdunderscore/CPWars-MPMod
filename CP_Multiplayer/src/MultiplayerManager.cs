using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CPMod_Multiplayer.LobbyManagement;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CPMod_Multiplayer
{
    struct StatusOverride
    {
        public int Power, Speed, Intelligence;
    }
    
    class MultiplayerManager : MonoBehaviour
    {
        public static MultiplayerManager instance;
        public static bool SteamNetworkAvailable = false;

        // We lock these in to ensure they don't revert if we're disconnected later
        private static bool _mpSession, _mpFollower;

        public List<string> clubList { get; set; } = new List<string>();

        public static bool MultiplayerSession
        {
            get => _mpSession || LobbyManager.CurrentLobby != null;
            private set => _mpSession = value;
        }

        public static bool MultiplayerFollower
        {
            get => _mpFollower || (LobbyManager.CurrentLobby != null && !LobbyManager.CurrentLobby.IsHost);
            private set => _mpFollower = value;
        }

        public static Boolean SuppressGameLogic => MultiplayerFollower;
        public static Boolean EveryoneIsPlayer => MultiplayerSession;
        public static int MyTeam { get; private set; } = 1;

        private Scene currentGameScene;
        public bool isPuppet;

        public Dictionary<string, StatusOverride> StatusOverrides = new Dictionary<string, StatusOverride>();

        private static int[] _money = new int[7];

        public static void InitMoney(int nTeams, int initialMoney)
        {
            _money = new int[nTeams + 1];
            for (int i = 0; i <= nTeams; i++) _money[i] = initialMoney;
        }

        public static int GetMoney(int team)
        {
            if (team > _money.Length) return 0;
            
            return _money[team];
        }

        public static int[] GetMoney()
        {
            return _money;
        }

        public static void SetMoney(int team, int money)
        {
            if (team > _money.Length) return;
            
            _money[team] = money;

            if (team == MyTeam)
            {
                GameManager.Instance.Money = money;
            }
        }
        
        public void SetupClubs(MemberSet members)
        {
            try
            {
                var availableClubs = ConstString.ClubName.Keys.Where(k => k != "---")
                    .ToList();

                var selectedClubs = new List<string>();
                selectedClubs.Add("---");
                foreach (var member in LobbyManager.CurrentLobby.Members)
                {
                    if (availableClubs.Contains(member.MemberState.selectedClub))
                    {
                        selectedClubs.Add(member.MemberState.selectedClub);
                        availableClubs.Remove(member.MemberState.selectedClub);
                    }
                    else
                    {
                        var randomClub = availableClubs[UnityEngine.Random.Range(0, availableClubs.Count)];
                        selectedClubs.Add(randomClub);
                        availableClubs.Remove(randomClub);
                    }
                }

                Mod.logger.Log("[MultiplayerManager] Set clublist=" + selectedClubs.Join(a => a, ","));
                clubList = selectedClubs;
            }
            catch (Exception e)
            {
                Mod.LogException("SetupClubs", e);
                throw e;
            }
        }
        
        public void Awake()
        {
            instance = this;
            
            DontDestroyOnLoad(this.gameObject);

            StartCoroutine(SteamInit());
        }

        IEnumerator SteamInit()
        {
            while (!SteamManager.Initialized)
            {
                yield return null;
            }
            
            SteamNetworkingUtils.InitRelayNetworkAccess();

            float lastTime = 0;
            bool polling = true;
            while (polling)
            {
                while (Time.fixedUnscaledTime - lastTime < 1)
                {
                    yield return null;
                }
                
                var status = SteamNetworkingUtils.GetRelayNetworkStatus(out var pDetails);
                switch (status)
                {
                    case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Attempting:
                    case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Waiting:
                        yield return null;
                        break;
                    default:
                        Mod.logger.Log($"Steam networking status: {status} eAvail={pDetails.m_eAvail} eAvailAnyRelay={pDetails.m_eAvailAnyRelay} eAvailNetConfig={pDetails.m_eAvailNetworkConfig} debugMsg={pDetails.m_debugMsg}");

                        if (pDetails.m_eAvail == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
                        {
                            SteamNetworkAvailable = true;
                            polling = false;
                        }
                        
                        break;
                }
            }
        }

        public void OnDestroy()
        {
            instance = null;
        }

        public void Update()
        {
            if (currentGameScene.IsValid()) return;
            
            Scene gameScene = SceneManager.GetSceneByName("GameScene");
            if (gameScene.IsValid())
            {
                currentGameScene = gameScene;
                
                var activeScene = SceneManager.GetActiveScene();
                try
                {
                    SceneManager.SetActiveScene(gameScene);
                    OnEnterGameScene();
                }
                finally
                {
                    SceneManager.SetActiveScene(activeScene);
                }

                return;
            }

            Scene titleScene = SceneManager.GetSceneByName("TitleScene");
            if (titleScene.IsValid())
            {
                currentGameScene = titleScene;
                
                var activeScene = SceneManager.GetActiveScene();
                try
                {
                    SceneManager.SetActiveScene(titleScene);
                    OnEnterTitleScene();
                }
                catch (Exception e)
                {
                    Mod.LogException("OnEnterTitleScene", e);
                }
                finally
                {
                    SceneManager.SetActiveScene(activeScene);
                }
            }
        }

        void OnEnterGameScene()
        {
            var lobby = LobbyManager.CurrentLobby;
            
            Mod.logger.Log($"[OnEnterGameScene] lobbypresent={lobby != null} isHost={lobby?.IsHost}");

            // Lock values
            MultiplayerSession = MultiplayerSession;
            MultiplayerFollower = MultiplayerFollower;

            if (!MultiplayerSession)
            {
                MyTeam = 1;
                return;
            }
            var netPuppet = new GameObject("NetworkPuppet");
            MyTeam = lobby.MyTeamIndex;
            Mod.logger.Log($"MyTeamIndex={MyTeam}");

            if (!MultiplayerFollower)
            {
                netPuppet.AddComponent<PuppetMaster>();
                netPuppet.name = "NetworkPuppetMaster";
            }
            else
            {
                try
                {
                    Mod.logger.Log("Creating puppet client");
                    netPuppet.AddComponent<PuppetClient>();
                }
                catch (Exception e)
                {
                    Mod.LogException("OnEnterGameScene: Create PuppetClient", e);
                }
            }
        }

        void OnEnterTitleScene()
        {
            if (MultiplayerSession)
            {
                // Reload character data
                SaveFileManager.Instance.LoadSavedatas();
                _mpFollower = _mpSession = false;
            }
            
            float margin = 50;
            
            var hajimeru = GameObject.Find("Button_Start");
            var top = hajimeru.transform.parent;

            var pos = hajimeru.transform.localPosition;
            var rectXform = hajimeru.GetComponent<RectTransform>();
            pos.x -= (rectXform.rect.width + margin) / 2;
            hajimeru.transform.localPosition = pos;

            var networkBtnPrefab = Mod.assetBundle.LoadAsset<GameObject>("Button_Multiplayer");
            Mod.logger.Log("Network button prefab: " + networkBtnPrefab);
            var networkBtn = Instantiate(networkBtnPrefab, top);
            
            networkBtn.transform.localPosition 
                = pos + new Vector3(1,0,0) * (rectXform.rect.width + margin);
            
            // Change order so it doesn't pop up over the settings window
            networkBtn.transform.SetSiblingIndex(hajimeru.transform.GetSiblingIndex() + 1);
            
            networkBtn.GetComponent<Button>().onClick.AddListener(() =>
            {
                SoundEffectManager.Instance.PlayOneShot("se_in");
                
                var mpWindow = MultiplayerSetupWindow.Create();
                mpWindow.gameObject.SetActive(true);
                top.gameObject.SetActive(false);
            });

            if (LobbyManager.CurrentLobby != null)
            {
                top.gameObject.SetActive(false);
                LobbyManager.CurrentLobby.OnGameOver();
                MultiplayerLobbyWindow.Create();
            }
            
            Mod.logger.Log("Title screen scene injection complete");
        }

        public static void SelectCharacterData(string charaName, int team)
        {
            if (!MultiplayerSession || MultiplayerFollower ||
                CharacterData.Instance.GetCharacters().ContainsKey(charaName))
            {
                return;
            }
            
            // TODO - select which player's character data should be used
        }
    }
}
