﻿using Assets.Scripts.Missions;
using Assets.Scripts.Pacing;
using Assets.Scripts.Props;
using Assets.Scripts.Records;
using Events;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;

namespace MultipleBombsAssembly
{
    public class MultipleBombs : MonoBehaviour
    {
        private int currentFreePlayBombCount;
        private int currentBombCount;
        private float? defaultMaxTime = null;
        private int currentMaxModModules;
        private TextMeshPro currentFreePlayBombCountLabel;
        private Dictionary<string, int> multipleBombsMissions;
        private Dictionary<GameplayRoom, int> multipleBombsRooms;
        private FieldInfo gameplayStateRoomGOField;
        private FieldInfo gameplayStateLightBulbField;
        private Dictionary<Bomb, BombEvents.BombSolvedEvent> bombSolvedEvents;
        private Dictionary<Bomb, BombComponentEvents.ComponentPassEvent> bombComponentPassEvents;
        private Dictionary<Bomb, BombComponentEvents.ComponentStrikeEvent> bombComponentStrikeEvents;
        private ResultPageMonitor freePlayDefusedPageMonitor;
        private ResultPageMonitor freePlayExplodedPageMonitor;
        private ResultPageMonitor missionDefusedPageMonitor;
        private ResultPageMonitor missionExplodedPageMonitor;
        private MissionDetailPageMonitor missionDetailPageMonitor;
        private KMGameInfo gameInfo;
        private KMGameCommands gameCommands;
        private MultipleBombsProperties publicProperties;

        public void Awake()
        {
            Debug.Log("[MultipleBombs]Initializing");
            DestroyImmediate(GetComponent<KMService>()); //Hide from Mod Selector
            currentFreePlayBombCount = 1;
            multipleBombsMissions = new Dictionary<string, int>();
            multipleBombsRooms = new Dictionary<GameplayRoom, int>();
            gameplayStateRoomGOField = typeof(GameplayState).GetField("roomGO", BindingFlags.Instance | BindingFlags.NonPublic);
            gameplayStateLightBulbField = typeof(GameplayState).GetField("lightBulb", BindingFlags.Instance | BindingFlags.NonPublic);
            gameInfo = GetComponent<KMGameInfo>();
            gameCommands = GetComponent<KMGameCommands>();
            gameInfo.OnStateChange += onGameStateChanged;

            GameObject infoObject = new GameObject("MultipleBombs_Info");
            infoObject.transform.parent = gameObject.transform;
            publicProperties = infoObject.AddComponent<MultipleBombsProperties>();
            publicProperties.MultipleBombs = this;

            Debug.Log("[MultipleBombs]Initialized");
        }

        public void Update()
        {
            if (SceneManager.Instance != null)
            {
                if (SceneManager.Instance.CurrentState == SceneManager.State.Setup)
                {
                    int maxBombCount = GetCurrentMaximumBombCount();
                    if (defaultMaxTime == null)
                        defaultMaxTime = FreeplayDevice.MAX_SECONDS_TO_SOLVE;
                    float newMaxTime = defaultMaxTime.Value * maxBombCount;
                    int maxModules = currentMaxModModules;
                    if (GameplayState.GameplayRoomPrefabOverride != null && GameplayState.GameplayRoomPrefabOverride.GetComponent<GameplayRoom>().BombPrefabOverride != null)
                    {
                        maxModules = Math.Max(GameplayState.GameplayRoomPrefabOverride.GetComponent<GameplayRoom>().BombPrefabOverride.GetComponent<Bomb>().GetMaxModules(), FreeplayDevice.MAX_MODULE_COUNT);
                        maxModules = Math.Max(currentMaxModModules, maxModules);
                    }
                    if (maxModules > FreeplayDevice.MAX_MODULE_COUNT)
                    {
                        newMaxTime += (maxModules - FreeplayDevice.MAX_MODULE_COUNT) * 60 *
                                      (maxBombCount - 1);
                    }
                    FreeplayDevice.MAX_SECONDS_TO_SOLVE = newMaxTime;

                    if (currentFreePlayBombCount > maxBombCount)
                    {
                        currentFreePlayBombCount = maxBombCount;
                        currentFreePlayBombCountLabel.text = currentFreePlayBombCount.ToString();
                    }
                }
            }
        }

        public void OnDestroy()
        {
            if (SceneManager.Instance.CurrentState != SceneManager.State.ModManager)
                throw new NotImplementedException();
            Debug.Log("[MultipleBombs]Destroying");
            foreach (Mission mission in ModManager.Instance.ModMissions)
            {
                if (multipleBombsMissions.ContainsKey(mission.ID))
                {
                    ComponentPool pool = new ComponentPool();
                    pool.ModTypes = new List<string>() { "Multiple Bombs" };
                    pool.Count = multipleBombsMissions[mission.ID] - 1;
                    mission.GeneratorSetting.ComponentPools.Add(pool);
                }
            }
            if (missionDetailPageMonitor != null)
            {
                Destroy(missionDetailPageMonitor);
            }
            if (freePlayDefusedPageMonitor != null)
            {
                Destroy(freePlayDefusedPageMonitor);
            }
            if (freePlayExplodedPageMonitor != null)
            {
                Destroy(freePlayDefusedPageMonitor);
            }
            if (missionDefusedPageMonitor != null)
            {
                Destroy(missionDefusedPageMonitor);
            }
            if (missionExplodedPageMonitor != null)
            {
                Destroy(missionDefusedPageMonitor);
            }
            gameInfo.OnStateChange -= onGameStateChanged;
            Debug.Log("[MultipleBombs]Destroyed");
        }

        private void onGameStateChanged(KMGameInfo.State state)
        {
            if (state == KMGameInfo.State.Gameplay)
            {
                if (GameplayState.MissionToLoad == FreeplayMissionGenerator.FREEPLAY_MISSION_ID || GameplayState.MissionToLoad != null && multipleBombsMissions.ContainsKey(GameplayState.MissionToLoad) || GameplayState.MissionToLoad == ModMission.CUSTOM_MISSION_ID)
                    StartCoroutine(setupGameplayState());
            }
            else
            {
                Debug.Log("[MultipleBombs]Cleaning up");
                StopAllCoroutines();
                currentBombCount = 1;
                bombSolvedEvents = null;
                bombComponentPassEvents = null;
                bombComponentStrikeEvents = null;
                BombEvents.OnBombDetonated -= onBombDetonated;
                BombComponentEvents.OnComponentPass -= onComponentPassEvent;
                BombComponentEvents.OnComponentStrike -= onComponentStrikeEvent;
                if (state == KMGameInfo.State.Setup)
                {
                    StartCoroutine(setupSetupRoom());
                }
            }
        }

        private IEnumerator setupSetupRoom()
        {
            yield return null;
            Debug.Log("[MultipleBombs]Adding FreePlay option");
            FreeplayDevice device = FindObjectOfType<FreeplayDevice>();
            if (device == null)
            {
                Debug.Log("[MultipleBombs]Error: FreePlayDevice not found!");
            }
            else
            {
                GameObject modulesObject = device.ModuleCountIncrement.transform.parent.gameObject;
                GameObject bombsObject = Instantiate(modulesObject, modulesObject.transform.position, modulesObject.transform.rotation, modulesObject.transform.parent);
                device.ObjectsToDisableOnLidClose.Add(bombsObject);
                bombsObject.name = "BombCountSettings";
                bombsObject.transform.localPosition += new Vector3(0, 0f, -0.025f);
                bombsObject.transform.Find("ModuleCountLabel").GetComponent<TextMeshPro>().text = "Bombs";
                currentFreePlayBombCountLabel = bombsObject.transform.Find("ModuleCountValue").GetComponent<TextMeshPro>();
                currentFreePlayBombCountLabel.gameObject.name = "BombCountLabel";
                currentFreePlayBombCountLabel.text = currentFreePlayBombCount.ToString();
                GameObject modulesLedShell = modulesObject.transform.parent.Find("LEDs/Modules_LED_shell").gameObject;
                GameObject bombsLedShell = Instantiate(modulesLedShell, modulesLedShell.transform.position, modulesLedShell.transform.rotation, modulesLedShell.transform.parent);
                bombsLedShell.name = "Bombs_LED_shell";
                bombsLedShell.transform.localPosition += new Vector3(0, 0, -0.028f);
                LED bombsLed = bombsObject.transform.Find("ModuleCountLED").GetComponent<LED>();
                bombsLed.gameObject.name = "BombCountLed";
                bombsLed.transform.localPosition += new Vector3(0, 0, 0.003f);
                bombsLed.transform.Find("Modules_LED_On").gameObject.name = "Bombs_LED_On";
                bombsLed.transform.Find("Modules_LED_Off").gameObject.name = "Bombs_LED_Off";
                bombsObject.transform.Find("Modules_INCR_btn_highlight").name = "Bombs_INCR_btn_highlight";
                bombsObject.transform.Find("Modules_DECR_btn_highlight").name = "Bombs_DECR_btn_highlight";

                GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
                background.name = "BombCountBackground";
                background.GetComponent<Renderer>().material.color = Color.black;
                background.transform.localScale = new Vector3(0.048f, 0.023f, 0.005f); //Accurate Y would be 0.025
                background.transform.parent = bombsObject.transform;
                background.transform.localPosition = currentFreePlayBombCountLabel.gameObject.transform.localPosition + new Vector3(0.00025f, -0.0027f, 0);
                background.transform.localEulerAngles = currentFreePlayBombCountLabel.gameObject.transform.localEulerAngles;

                GameObject incrementButton = bombsObject.transform.Find("Modules_INCR_btn").gameObject;
                incrementButton.name = "Bombs_INCR_btn";
                GameObject decrementButton = bombsObject.transform.Find("Modules_DECR_btn").gameObject;
                decrementButton.name = "Bombs_DECR_btn";
                Selectable deviceSelectable = device.GetComponent<Selectable>();
                List<Selectable> children = deviceSelectable.Children.ToList();
                children.Insert(2, incrementButton.GetComponent<Selectable>());
                children.Insert(2, decrementButton.GetComponent<Selectable>());
                deviceSelectable.Children = children.ToArray();
                deviceSelectable.Init();
                incrementButton.GetComponent<KeypadButton>().OnPush = new PushEvent(() =>
                {
                    if (currentFreePlayBombCount >= GetCurrentMaximumBombCount())
                        return;
                    currentFreePlayBombCount++;
                    currentFreePlayBombCountLabel.text = currentFreePlayBombCount.ToString();
                });
                decrementButton.GetComponent<KeypadButton>().OnPush = new PushEvent(() =>
                {
                    if (currentFreePlayBombCount <= 1)
                        return;
                    currentFreePlayBombCount--;
                    currentFreePlayBombCountLabel.text = currentFreePlayBombCount.ToString();
                });
                //string textColor = "#" + valueText.color.r.ToString("x2") + valueText.color.g.ToString("x2") + valueText.color.b.ToString("x2");
                incrementButton.GetComponent<Selectable>().OnHighlight = new Action(() =>
                {
                    device.Screen.CurrentState = FreeplayScreen.State.Start;
                    bombsLed.SetState(true);
                    device.Screen.ScreenText.text = "BOMBS:\n\nNumber of bombs\nto defuse\n\n<size=20><#00ff00>Multiple Bombs Mod</color></size>";
                });
                decrementButton.GetComponent<Selectable>().OnHighlight = new Action(() =>
                {
                    device.Screen.CurrentState = FreeplayScreen.State.Start;
                    bombsLed.SetState(true);
                    device.Screen.ScreenText.text = "BOMBS:\n\nNumber of bombs\nto defuse\n\n<size=20><#00ff00>Multiple Bombs Mod</color></size>";
                });

                Action disableBomsLed = new Action(() => bombsLed.SetState(false));
                device.ModuleCountIncrement.GetComponent<Selectable>().OnHighlight = (Action)Delegate.Combine(new Action(() =>
                {
                    device.Screen.CurrentState = FreeplayScreen.State.Modules;
                    device.Screen.ScreenText.text = "MODULES:\n\nNumber of modules\nper bomb";
                }), disableBomsLed);
                device.ModuleCountDecrement.GetComponent<Selectable>().OnHighlight = (Action)Delegate.Combine(new Action(() =>
                {
                    device.Screen.CurrentState = FreeplayScreen.State.Modules;
                    device.Screen.ScreenText.text = "MODULES:\n\nNumber of modules\nper bomb";
                }), disableBomsLed);
                device.TimeDecrement.GetComponent<Selectable>().OnHighlight += disableBomsLed;
                device.TimeIncrement.GetComponent<Selectable>().OnHighlight += disableBomsLed;
                device.NeedyToggle.GetComponent<Selectable>().OnHighlight += disableBomsLed;
                device.HardcoreToggle.GetComponent<Selectable>().OnHighlight += disableBomsLed;
                device.ModsOnly.GetComponent<Selectable>().OnHighlight += disableBomsLed;
                device.StartButton.GetComponent<Selectable>().OnHighlight += disableBomsLed;

                incrementButton.GetComponent<Selectable>().SelectableArea.GetComponent<BoxCollider>().size += new Vector3(-0.015f, -0.015f, -0.015f);
                decrementButton.GetComponent<Selectable>().SelectableArea.GetComponent<BoxCollider>().size += new Vector3(-0.015f, -0.015f, -0.015f);
                device.ModuleCountIncrement.GetComponent<Selectable>().SelectableArea.GetComponent<BoxCollider>().size += new Vector3(-0.012f, -0.012f, -0.012f);
                device.ModuleCountDecrement.GetComponent<Selectable>().SelectableArea.GetComponent<BoxCollider>().size += new Vector3(-0.012f, -0.012f, -0.012f);
                device.TimeIncrement.GetComponent<Selectable>().SelectableArea.GetComponent<BoxCollider>().size += new Vector3(-0.01f, -0.01f, -0.01f);
                device.TimeDecrement.GetComponent<Selectable>().SelectableArea.GetComponent<BoxCollider>().size += new Vector3(-0.01f, -0.01f, -0.01f);
                Debug.Log("[MultipleBombs]FreePlay option added");

                currentMaxModModules = ModManager.Instance.GetMaximumModules();

                foreach (ModMission mission in ModManager.Instance.ModMissions)
                {
                    if (!multipleBombsMissions.ContainsKey(mission.ID))
                    {
                        int missionBombCount = ProcessMultipleBombsMission(mission);
                        if (missionBombCount >= 2)
                        {
                            multipleBombsMissions.Add(mission.ID, missionBombCount);
                        }
                    }
                }
                Debug.Log("[MultipleBombs]Missions processed");

                multipleBombsRooms = new Dictionary<GameplayRoom, int>();
                foreach (GameplayRoom room in ModManager.Instance.GetGameplayRooms())
                {
                    for (int i = 2; i < int.MaxValue; i++)
                    {
                        if (!room.transform.FindRecursive("MultipleBombs_Spawn_" + i))
                        {
                            multipleBombsRooms.Add(room, i);
                            break;
                        }
                    }
                }
                Debug.Log("[MultipleBombs]GamePlayRooms processed");

                if (missionDetailPageMonitor == null)
                {
                    missionDetailPageMonitor = FindObjectOfType<SetupRoom>().BombBinder.MissionDetailPage.gameObject.AddComponent<MissionDetailPageMonitor>();
                    missionDetailPageMonitor.MultipleBombs = this;
                }
                missionDetailPageMonitor.MissionList = multipleBombsMissions;
                Debug.Log("[MultipleBombs]BombBinder info added");
            }
        }

        private int ProcessMultipleBombsMission(Mission mission)
        {
            List<ComponentPool> pools;
            return ProcessMultipleBombsMission(mission, out pools);
        }

        private int ProcessMultipleBombsMission(Mission mission, out List<ComponentPool> bombsPools)
        {
            int count = 1;
            bombsPools = new List<ComponentPool>();
            for (int i = mission.GeneratorSetting.ComponentPools.Count - 1; i >= 0; i--)
            {
                ComponentPool pool = mission.GeneratorSetting.ComponentPools[i];
                if (pool.ModTypes != null && pool.ModTypes.Count == 1 && pool.ModTypes[0] == "Multiple Bombs")
                {
                    mission.GeneratorSetting.ComponentPools.RemoveAt(i);
                    count += pool.Count;
                    bombsPools.Add(pool);
                }
            }
            return count;
        }

        private IEnumerator setupGameplayState()
        {
            currentBombCount = 1;
            List<ComponentPool> customMissionBombsPools = null;
            if (GameplayState.MissionToLoad == FreeplayMissionGenerator.FREEPLAY_MISSION_ID)
                currentBombCount = currentFreePlayBombCount;
            else if (multipleBombsMissions.ContainsKey(GameplayState.MissionToLoad))
                currentBombCount = multipleBombsMissions[GameplayState.MissionToLoad];
            else if (GameplayState.MissionToLoad == ModMission.CUSTOM_MISSION_ID)
                currentBombCount = ProcessMultipleBombsMission(GameplayState.CustomMission, out customMissionBombsPools);

            if (currentBombCount > 2 && GameplayState.GameplayRoomPrefabOverride == null)
            {
                Debug.Log("[MultipleBombs]Initializing room");
                GameplayRoom roomPrefab = GetRandomGameplayRoom(currentBombCount);
                if (roomPrefab != null)
                {
                    Destroy((GameObject)gameplayStateRoomGOField.GetValue(SceneManager.Instance.GameplayState));
                    GameObject room = Instantiate(roomPrefab.gameObject, Vector3.zero, Quaternion.identity);
                    room.transform.parent = SceneManager.Instance.GameplayState.transform;
                    room.transform.localScale = Vector3.one;
                    gameplayStateRoomGOField.SetValue(SceneManager.Instance.GameplayState, room);
                    gameplayStateLightBulbField.SetValue(SceneManager.Instance.GameplayState, GameObject.Find("LightBulb"));
                    room.SetActive(false);
                    FindObjectOfType<BombGenerator>().BombPrefabOverride = room.GetComponent<GameplayRoom>().BombPrefabOverride;
                    Debug.Log("[MultipleBombs]Room initialized");
                }
                else
                {
                    Debug.Log("[MultipleBombs]No room found that supports " + currentBombCount + " bombs");
                }
            }

            BombInfoRedirection.SetBombCount(currentBombCount);
            if (bombSolvedEvents == null)
                bombSolvedEvents = new Dictionary<Bomb, BombEvents.BombSolvedEvent>();
            if (bombComponentPassEvents == null)
                bombComponentPassEvents = new Dictionary<Bomb, BombComponentEvents.ComponentPassEvent>();
            if (bombComponentStrikeEvents == null)
                bombComponentStrikeEvents = new Dictionary<Bomb, BombComponentEvents.ComponentStrikeEvent>();

            BombEvents.OnBombDetonated += onBombDetonated;
            BombComponentEvents.OnComponentPass += onComponentPassEvent;
            BombComponentEvents.OnComponentStrike += onComponentStrikeEvent;
            Debug.Log("[MultipleBombs]Events initialized");

            //Setup results screen
            if (freePlayDefusedPageMonitor == null)
            {
                freePlayDefusedPageMonitor = SceneManager.Instance.PostGameState.Room.BombBinder.ResultFreeplayDefusedPage.gameObject.AddComponent<ResultPageMonitor>();
                freePlayDefusedPageMonitor.MultipleBombs = this;
            }
            if (freePlayExplodedPageMonitor == null)
            {
                freePlayExplodedPageMonitor = SceneManager.Instance.PostGameState.Room.BombBinder.ResultFreeplayExplodedPage.gameObject.AddComponent<ResultPageMonitor>();
                freePlayExplodedPageMonitor.MultipleBombs = this;
            }
            freePlayDefusedPageMonitor.SetBombCount(currentBombCount);
            freePlayExplodedPageMonitor.SetBombCount(currentBombCount);
            if (missionDefusedPageMonitor == null)
            {
                missionDefusedPageMonitor = SceneManager.Instance.PostGameState.Room.BombBinder.ResultDefusedPage.gameObject.AddComponent<ResultPageMonitor>();
                missionDefusedPageMonitor.MultipleBombs = this;
            }
            if (missionExplodedPageMonitor == null)
            {
                missionExplodedPageMonitor = SceneManager.Instance.PostGameState.Room.BombBinder.ResultExplodedPage.gameObject.AddComponent<ResultPageMonitor>();
                missionExplodedPageMonitor.MultipleBombs = this;
            }
            missionDefusedPageMonitor.SetBombCount(currentBombCount);
            missionExplodedPageMonitor.SetBombCount(currentBombCount);
            Debug.Log("[MultipleBombs]Result screens initialized");
            yield return null;
            Debug.Log("[MultipleBombs]Initializing gameplay state");

            Debug.Log("[MultipleBombs]Bombs to spawn: " + currentBombCount);

            if (currentBombCount == 1)
            {
                if (GameplayState.MissionToLoad == ModMission.CUSTOM_MISSION_ID)
                    GameplayState.CustomMission.GeneratorSetting.ComponentPools.AddRange(customMissionBombsPools);
                yield break;
            }

            List<KMBombInfo> redirectedBombInfos = new List<KMBombInfo>();

            Bomb vanillaBomb = FindObjectOfType<Bomb>();
            GameObject spawn0 = GameObject.Find("MultipleBombs_Spawn_0");
            if (spawn0 != null)
            {
                vanillaBomb.gameObject.transform.position = spawn0.transform.position;
                vanillaBomb.gameObject.transform.rotation = spawn0.transform.rotation;
            }
            else
            {
                vanillaBomb.gameObject.transform.position += new Vector3(-0.4f, 0, 0);
                vanillaBomb.gameObject.transform.eulerAngles += new Vector3(0, -30, 0);
            }
            vanillaBomb.GetComponent<FloatingHoldable>().Initialize();
            redirectPresentBombInfos(vanillaBomb, redirectedBombInfos);
            processBombEvents(vanillaBomb);
            Debug.Log("[MultipleBombs]Default bomb initialized");

            for (int i = currentBombCount - 1; i >= 1; i--)
            {
                GameObject spawn = GameObject.Find("MultipleBombs_Spawn_" + i);
                if (spawn == null)
                {
                    if (i == 1)
                    {
                        StartCoroutine(createNewBomb(GameplayState.MissionToLoad, SceneManager.Instance.GameplayState.Room.BombSpawnPosition.transform.position + new Vector3(0.4f, 0, 0), new Vector3(0, 30, 0), redirectedBombInfos));
                    }
                    else
                    {
                        Debug.LogError("[MultipleBombs]The current gameplay room doesn't support " + (i + 1) + " bombs");
                        break;
                    }
                }
                else
                {
                    StartCoroutine(createNewBomb(GameplayState.MissionToLoad, spawn.transform.position, spawn.transform.eulerAngles, redirectedBombInfos));
                }
            }

            if (GameplayState.MissionToLoad == ModMission.CUSTOM_MISSION_ID)
                GameplayState.CustomMission.GeneratorSetting.ComponentPools.AddRange(customMissionBombsPools);

            vanillaBomb.GetComponent<Selectable>().Parent.Init();
            Debug.Log("[MultipleBombs]All bombs generated");

            //PaceMaker
            PaceMakerMonitor monitor = FindObjectOfType<PaceMaker>().gameObject.AddComponent<PaceMakerMonitor>();
            foreach (Bomb bomb in FindObjectsOfType<Bomb>())
            {
                if (bomb != vanillaBomb) //The vanilla bomb is still handled by PaceMaker
                    bomb.GetTimer().TimerTick = (TimerComponent.TimerTickEvent)Delegate.Combine(bomb.GetTimer().TimerTick, new TimerComponent.TimerTickEvent((elapsed, remaining) => monitor.OnBombTimerTick(bomb, elapsed, remaining)));
            }
            Debug.Log("[MultipleBombs]Pacing events initalized");
        }

        private bool onComponentPass(BombComponent source)
        {
            Debug.Log("[MultipleBombs]A component was solved");
            if (source.Bomb.HasDetonated)
                return false;
            RecordManager.Instance.RecordModulePass();
            if (source.Bomb.IsSolved())
            {
                Debug.Log("[MultipleBombs]A bomb was solved (A winner is you!!)");
                source.Bomb.GetTimer().StopTimer();
                source.Bomb.GetTimer().Blink(1.5f);
                DarkTonic.MasterAudio.MasterAudio.PlaySound3DAtTransformAndForget("bomb_defused", source.Bomb.transform, 1f, null, 0f, null);
                if (BombEvents.OnBombSolved != null)
                    BombEvents.OnBombSolved();
                if (bombSolvedEvents.ContainsKey(source.Bomb) && bombSolvedEvents[source.Bomb] != null)
                    bombSolvedEvents[source.Bomb].Invoke();
                SceneManager.Instance.GameplayState.OnBombSolved();
                foreach (Bomb bomb in SceneManager.Instance.GameplayState.Bombs)
                    if (!bomb.IsSolved())
                        return true;
                Debug.Log("[MultipleBombs]All bombs solved, what a winner!");
                RecordManager.Instance.SetResult(GameResultEnum.Defused, source.Bomb.GetTimer().TimeElapsed, SceneManager.Instance.GameplayState.GetElapsedRealTime());
                return true;
            }
            return false;
        }

        private void onComponentPassEvent(BombComponent component, bool finalPass)
        {
            if (bombComponentPassEvents.ContainsKey(component.Bomb) && bombComponentPassEvents[component.Bomb] != null)
                bombComponentPassEvents[component.Bomb].Invoke(component, finalPass);
        }

        private void onComponentStrikeEvent(BombComponent component, bool finalStrike)
        {
            GameRecord currentRecord = RecordManager.Instance.GetCurrentRecord();
            if (!finalStrike && currentRecord.GetStrikeCount() == currentRecord.Strikes.Length)
            {
                List<StrikeSource> strikes = currentRecord.Strikes.ToList();
                strikes.Add(null);
                currentRecord.Strikes = strikes.ToArray();
            }
            if (bombComponentStrikeEvents.ContainsKey(component.Bomb) && bombComponentStrikeEvents[component.Bomb] != null)
                bombComponentStrikeEvents[component.Bomb].Invoke(component, finalStrike);
        }

        private void onBombDetonated()
        {
            foreach (Bomb bomb in SceneManager.Instance.GameplayState.Bombs)
            {
                bomb.GetTimer().StopTimer();
            }
            if (RecordManager.Instance.GetCurrentRecord().Result == GameResultEnum.ExplodedDueToStrikes)
            {
                float timeElapsed = 0;
                foreach (Bomb bomb in SceneManager.Instance.GameplayState.Bombs)
                {
                    float bombTime = bomb.GetTimer().TimeElapsed;
                    if (bombTime > timeElapsed)
                        timeElapsed = bombTime;
                }
                RecordManager.Instance.SetResult(GameResultEnum.ExplodedDueToStrikes, timeElapsed, SceneManager.Instance.GameplayState.GetElapsedRealTime());
            }
        }

        private IEnumerator createNewBomb(string missionId, Vector3 position, Vector3 eulerAngles, List<KMBombInfo> redirectedBombInfos)
        {
            Debug.Log("[MultipleBombs]Generating new bomb");

            GameplayState gameplayState = SceneManager.Instance.GameplayState;

            Bomb bomb = createBomb(missionId, position, eulerAngles, redirectedBombInfos);

            GameObject roomGO = (GameObject)gameplayStateRoomGOField.GetValue(gameplayState);
            Selectable mainSelectable = gameplayState.Room.GetComponent<Selectable>();
            List<Selectable> children = mainSelectable.Children.ToList();
            int row = 0;
            for (int i = mainSelectable.ChildRowLength; i <= children.Count; i += mainSelectable.ChildRowLength)
            {
                if (row != gameplayState.Room.BombSpawnPosition.SelectableIndexY)
                {
                    children.Insert(i, null);
                    i++;
                }
                row++;
            }
            mainSelectable.ChildRowLength++;
            mainSelectable.DefaultSelectableIndex = gameplayState.Room.BombSpawnPosition.SelectableIndexY * mainSelectable.ChildRowLength + gameplayState.Room.BombSpawnPosition.SelectableIndexX;
            children.Insert(mainSelectable.DefaultSelectableIndex + 1, bomb.GetComponent<Selectable>());
            mainSelectable.Children = children.ToArray();
            bomb.GetComponent<Selectable>().Parent = mainSelectable;
            KTInputManager.Instance.SelectableManager.ConfigureSelectableAreas(KTInputManager.Instance.RootSelectable);

            Debug.Log("[MultipleBombs]Bomb generated");
            yield return new WaitForSeconds(2f);

            Debug.Log("[MultipleBombs]Activating custom bomb timer");
            bomb.GetTimer().text.gameObject.SetActive(true);
            bomb.GetTimer().LightGlow.enabled = true;
            Debug.Log("[MultipleBombs]Custom bomb timer activated");
        }

        private Bomb createBomb(string missionId, Vector3 position, Vector3 eulerAngles, List<KMBombInfo> knownBombInfos)
        {
            if (bombSolvedEvents == null)
                bombSolvedEvents = new Dictionary<Bomb, BombEvents.BombSolvedEvent>();
            if (bombComponentPassEvents == null)
                bombComponentPassEvents = new Dictionary<Bomb, BombComponentEvents.ComponentPassEvent>();
            if (bombComponentStrikeEvents == null)
                bombComponentStrikeEvents = new Dictionary<Bomb, BombComponentEvents.ComponentStrikeEvent>();

            Debug.Log("[MultipleBombs]Creating new bomb");

            if (knownBombInfos == null)
            {
                knownBombInfos = new List<KMBombInfo>();
                foreach (KMBombInfo info in FindObjectsOfType<KMBombInfo>())
                {
                    knownBombInfos.Add(info);
                }
            }

            GameObject spawnPointGO = new GameObject("CustomBombSpawnPoint");
            spawnPointGO.transform.position = position;
            spawnPointGO.transform.eulerAngles = eulerAngles;
            HoldableSpawnPoint spawnPoint = spawnPointGO.AddComponent<HoldableSpawnPoint>();
            spawnPoint.HoldableTarget = SceneManager.Instance.GameplayState.Room.BombSpawnPosition.HoldableTarget;

            Bomb bomb = null;
            if (missionId == FreeplayMissionGenerator.FREEPLAY_MISSION_ID)
            {
                Mission freeplayMission = FreeplayMissionGenerator.Generate(GameplayState.FreeplaySettings);
                MissionManager.Instance.MissionDB.AddMission(freeplayMission);
                bomb = gameCommands.CreateBomb(missionId, null, spawnPointGO, new System.Random().Next().ToString()).GetComponent<Bomb>();
                MissionManager.Instance.MissionDB.Missions.Remove(freeplayMission);
            }
            else if (missionId == ModMission.CUSTOM_MISSION_ID)
            {
                Mission customMission = SceneManager.Instance.GameplayState.Mission;
                string oldName = customMission.name;
                customMission.name = ModMission.CUSTOM_MISSION_ID;
                MissionManager.Instance.MissionDB.AddMission(customMission);
                bomb = gameCommands.CreateBomb(missionId, null, spawnPointGO, new System.Random().Next().ToString()).GetComponent<Bomb>();
                MissionManager.Instance.MissionDB.Missions.Remove(customMission);
                customMission.name = oldName;
            }
            else
            {
                bomb = gameCommands.CreateBomb(missionId, null, spawnPointGO, new System.Random().Next().ToString()).GetComponent<Bomb>();
            }
            Debug.Log("[MultipleBombs]Bomb spawned");

            bomb.GetTimer().text.gameObject.SetActive(false);
            bomb.GetTimer().LightGlow.enabled = false;
            Debug.Log("[MultipleBombs]Timer deactivated");

            redirectPresentBombInfos(bomb, knownBombInfos);
            Debug.Log("[MultipleBombs]KMBombInfos redirected");

            processBombEvents(bomb);
            Debug.Log("[MultipleBombs]Bomb created");
            return bomb;
        }

        private void redirectPresentBombInfos(Bomb bomb, List<KMBombInfo> knownBombInfos)
        {
            if (knownBombInfos == null)
                knownBombInfos = new List<KMBombInfo>();
            foreach (KMBombInfo info in FindObjectsOfType<KMBombInfo>())
            {
                if (!knownBombInfos.Contains(info))
                {
                    info.TimeHandler = new KMBombInfo.GetTimeHandler(() => BombInfoRedirection.GetTime(bomb));
                    info.FormattedTimeHandler = new KMBombInfo.GetFormattedTimeHandler(() => BombInfoRedirection.GetFormattedTime(bomb));
                    info.StrikesHandler = new KMBombInfo.GetStrikesHandler(() => BombInfoRedirection.GetStrikes(bomb));
                    info.ModuleNamesHandler = new KMBombInfo.GetModuleNamesHandler(() => BombInfoRedirection.GetModuleNames(bomb));
                    info.SolvableModuleNamesHandler = new KMBombInfo.GetSolvableModuleNamesHandler(() => BombInfoRedirection.GetSolvableModuleNames(bomb));
                    info.SolvedModuleNamesHandler = new KMBombInfo.GetSolvedModuleNamesHandler(() => BombInfoRedirection.GetSolvedModuleNames(bomb));
                    info.WidgetQueryResponsesHandler = new KMBombInfo.GetWidgetQueryResponsesHandler((string queryKey, string queryInfo) => BombInfoRedirection.GetWidgetQueryResponses(bomb, queryKey, queryInfo));
                    info.IsBombPresentHandler = new KMBombInfo.KMIsBombPresent(() => BombInfoRedirection.IsBombPresent(bomb));
                    ModBombInfo modInfo = info.GetComponent<ModBombInfo>();
                    foreach (BombEvents.BombSolvedEvent bombSolvedDelegate in BombEvents.OnBombSolved.GetInvocationList())
                    {
                        if (bombSolvedDelegate.Target != null && ReferenceEquals(bombSolvedDelegate.Target, modInfo))
                        {
                            if (bombSolvedEvents.ContainsKey(bomb))
                                bombSolvedEvents[bomb] += bombSolvedDelegate;
                            else
                                bombSolvedEvents.Add(bomb, bombSolvedDelegate);
                            break;
                        }
                    }
                    knownBombInfos.Add(info);
                }
            }
        }

        private void processBombEvents(Bomb bomb)
        {
            List<Delegate> bombSolvedDelegates = BombEvents.OnBombSolved.GetInvocationList().ToList();
            for (int i = bombSolvedDelegates.Count - 1; i >= 0; i--)
            {
                BombEvents.BombSolvedEvent bombSolvedDelegate = (BombEvents.BombSolvedEvent)bombSolvedDelegates[i];
                if (bombSolvedDelegate.Target != null && (ReferenceEquals(bombSolvedDelegate.Target, bomb.GetTimer()) || ReferenceEquals(bombSolvedDelegate.Target, bomb.StrikeIndicator)))
                {
                    BombEvents.OnBombSolved -= bombSolvedDelegate;
                    if (bombSolvedEvents.ContainsKey(bomb))
                        bombSolvedEvents[bomb] += bombSolvedDelegate;
                    else
                        bombSolvedEvents.Add(bomb, bombSolvedDelegate);
                    bombSolvedDelegates.RemoveAt(i);
                }
            }
            List<Delegate> componentPassDelegates = BombComponentEvents.OnComponentPass.GetInvocationList().ToList();
            List<Delegate> componentStrikeDelegates = BombComponentEvents.OnComponentStrike.GetInvocationList().ToList();
            foreach (BombComponent component in bomb.BombComponents)
            {
                component.OnPass = onComponentPass;
                if (component is NeedyComponent)
                {
                    for (int i = bombSolvedDelegates.Count - 1; i >= 0; i--)
                    {
                        BombEvents.BombSolvedEvent bombSolvedDelegate = (BombEvents.BombSolvedEvent)bombSolvedDelegates[i];
                        if (bombSolvedDelegate != null && ReferenceEquals(bombSolvedDelegate.Target, component))
                        {
                            BombEvents.OnBombSolved -= bombSolvedDelegate;
                            if (bombSolvedEvents.ContainsKey(bomb))
                                bombSolvedEvents[bomb] += bombSolvedDelegate;
                            else
                                bombSolvedEvents.Add(bomb, bombSolvedDelegate);
                            bombSolvedDelegates.RemoveAt(i);
                            break;
                        }
                    }
                    for (int i = componentPassDelegates.Count - 1; i >= 0; i--)
                    {
                        BombComponentEvents.ComponentPassEvent componentPassEvent = (BombComponentEvents.ComponentPassEvent)componentPassDelegates[i];
                        if (componentPassEvent.Target != null && ReferenceEquals(componentPassEvent.Target, component))
                        {
                            BombComponentEvents.OnComponentPass -= componentPassEvent;
                            if (bombComponentPassEvents.ContainsKey(bomb))
                                bombComponentPassEvents[bomb] += componentPassEvent;
                            else
                                bombComponentPassEvents.Add(bomb, componentPassEvent);
                            componentPassDelegates.RemoveAt(i);
                            break;
                        }
                    }
                    for (int i = componentStrikeDelegates.Count - 1; i >= 0; i--)
                    {
                        BombComponentEvents.ComponentStrikeEvent componentStrikeEvent = (BombComponentEvents.ComponentStrikeEvent)componentStrikeDelegates[i];
                        if (componentStrikeEvent.Target != null && ReferenceEquals(componentStrikeEvent.Target, component))
                        {
                            BombComponentEvents.OnComponentStrike -= componentStrikeEvent;
                            if (bombComponentStrikeEvents.ContainsKey(bomb))
                                bombComponentStrikeEvents[bomb] += componentStrikeEvent;
                            else
                                bombComponentStrikeEvents.Add(bomb, componentStrikeEvent);
                            componentStrikeDelegates.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        public int GetCurrentMaximumBombCount()
        {
            if (GameplayState.GameplayRoomPrefabOverride != null)
            {
                if (GameplayState.GameplayRoomPrefabOverride.GetComponent<ElevatorRoom>() != null)
                    return 1;
                GameplayRoom room = GameplayState.GameplayRoomPrefabOverride.GetComponent<GameplayRoom>();
                if (multipleBombsRooms.ContainsKey(room))
                {
                    return multipleBombsRooms[room];
                }
                return 2;
            }
            else
            {
                int max = 2;
                foreach (int count in multipleBombsRooms.Values)
                {
                    if (count > max)
                        max = count;
                }
                return max;
            }
        }

        private GameplayRoom GetRandomGameplayRoom(int bombs)
        {
            if (bombs > 2)
            {
                List<GameplayRoom> rooms = new List<GameplayRoom>();
                foreach (KeyValuePair<GameplayRoom, int> room in multipleBombsRooms)
                {
                    if (room.Value >= bombs)
                        rooms.Add(room.Key);
                }
                if (rooms.Count == 0)
                    return null;
                return rooms[UnityEngine.Random.Range(0, rooms.Count)];
            }
            return null;
        }

        public int CurrentFreePlayBombCount
        {
            get
            {
                return currentFreePlayBombCount;
            }
            set
            {
                if (SceneManager.Instance.CurrentState != SceneManager.State.Setup)
                    throw new InvalidOperationException("You can only set the current FreePlay bomb count in the Setup room.");
                if (value < 1)
                    throw new Exception("The bomb count must be greater than 0.");
                if (value > GetCurrentMaximumBombCount())
                    throw new Exception("The specified bomb count is greater than the current maximum bomb count.");
                currentFreePlayBombCount = value;
                currentFreePlayBombCountLabel.text = currentFreePlayBombCount.ToString();
            }
        }
    }
}
