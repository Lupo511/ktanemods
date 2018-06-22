﻿using Assets.Scripts.Missions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MultipleBombsAssembly
{
    public class MultipleBombsMissionDetails
    {
        public int BombCount { get; set; } = 1;
        public Dictionary<int, GeneratorSetting> GeneratorSettings { get; set; } = new Dictionary<int, GeneratorSetting>();

        public MultipleBombsMissionDetails()
        {

        }

        public MultipleBombsMissionDetails(int bombCount, GeneratorSetting firstBombGeneratorSetting)
        {
            BombCount = bombCount;
            GeneratorSettings.Add(0, firstBombGeneratorSetting);
        }

        public static MultipleBombsMissionDetails ReadMission(Mission mission)
        {
            MultipleBombsMissionDetails missionDetails = new MultipleBombsMissionDetails();
            if (mission.GeneratorSetting != null)
            {
                GeneratorSetting generatorSetting = UnityEngine.Object.Instantiate(mission).GeneratorSetting;
                missionDetails.GeneratorSettings.Add(0, generatorSetting);
                if (generatorSetting.ComponentPools != null)
                {
                    for (int i = generatorSetting.ComponentPools.Count - 1; i >= 0; i--)
                    {
                        ComponentPool pool = generatorSetting.ComponentPools[i];
                        if (pool.ModTypes != null && pool.ModTypes.Count == 1 && pool.ModTypes[0] == "Multiple Bombs")
                        {
                            missionDetails.BombCount += pool.Count;
                            generatorSetting.ComponentPools.RemoveAt(i);
                        }
                    }
                }
            }
            else
            {
                missionDetails.GeneratorSettings.Add(0, null);
            }
            return missionDetails;
        }
    }
}
