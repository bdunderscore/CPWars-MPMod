using System;
using System.Collections;
using System.Collections.Generic;
using CPMod_Multiplayer.LobbyManagement;
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

        public static bool MultiplayerSession => LobbyManager.CurrentLobby != null;
        public static bool MultiplayerFollower => MultiplayerSession && !LobbyManager.CurrentLobby.IsHost;

        public static Boolean SuppressGameLogic => MultiplayerFollower;
        public static Boolean EveryoneIsPlayer => MultiplayerSession;
        
        private Scene currentGameScene;
        public bool isPuppet;

        public Dictionary<string, StatusOverride> StatusOverrides = new Dictionary<string, StatusOverride>();

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

            if (!MultiplayerSession)
            {
                return;
            }
            var netPuppet = new GameObject("NetworkPuppet");

            if (!MultiplayerFollower)
            {
                netPuppet.AddComponent<PuppetMaster>();
                netPuppet.name = "NetworkPuppetMaster";
            }
            else
            {
                Mod.logger.Log("Creating puppet client");
                netPuppet.AddComponent<PuppetClient>();
            }
        }

        void OnEnterTitleScene()
        {
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
    }
}
