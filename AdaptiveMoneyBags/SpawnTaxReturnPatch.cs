using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace AdaptiveMoneyBags
{
    [HarmonyPatch(typeof(ExtractionPoint), "SpawnTaxReturn")]
    public static class SpawnTaxReturnPatch
    {
        // ============================================================
        //  TUNABLE CONSTANTS
        // ============================================================

        private const int SingleBagMaxValue = 13000;
        private const int VanillaMaxValue = 10000;
        private const int MinSplitBagValue = 8000;
        private const int AverageSplitBagValue = 10000;

        private const int MaxBagsNormal = 5;
        private const int MaxBagsHuge = 7;
        private const int HugeSurplusThreshold = 200000;

        private const float ValueVariation = 0.30f;

        // --- Scale curve: now THREE segments ---
        //   [150$   .. 2000$]   -> [0.25 .. 0.55]   (tiny -> noticeable)
        //   [2000$  .. 10000$]  -> [0.55 .. 1.00]   (noticeable -> vanilla big)
        //   [10000$ .. 13000$]  -> [1.00 .. 1.30]   (vanilla big -> slightly enlarged)

        // Smallest visual scale. Bumped from 0.15 to 0.25 so cheap bags
        // are still clearly visible and don't look like dust.
        private const float MinScale = 0.25f;

        // Mid-point on the low end: bag worth ~2000$ looks "noticeable but small".
        private const float LowMidScale = 0.55f;
        private const float LowMidValue = 2000f;

        // Vanilla big bag scale.
        private const float VanillaScale = 1.0f;

        // Max enlarged scale. Texture stretched slightly but still looks fine.
        private const float MaxEnlargedScale = 1.30f;

        // Value range for the low curve segment.
        private const float ScaleMinValue = 150f;

        // Vanilla big bag value (used as a breakpoint for scale mapping).
        private const float ScaleMaxValue = 10000f;

        // Circle radius for spreading multiple bags.
        private const float SpawnRadius = 0.55f;

        // Vertical step between bags.
        private const float SpawnHeightStep = 0.18f;

        // ============================================================

        static bool Prefix(ExtractionPoint __instance)
        {
            int surplus = RoundDirector.instance.extractionPointSurplus;

            if (surplus > 0)
            {
                if (SemiFunc.IsMasterClientOrSingleplayer())
                {
                    if (surplus <= SingleBagMaxValue)
                    {
                        SpawnBag(__instance, surplus, 0, 1, false);
                    }
                    else
                    {
                        List<int> bagValues = DistributeValues(surplus);
                        for (int i = 0; i < bagValues.Count; i++)
                            SpawnBag(__instance, bagValues[i], i, bagValues.Count, true);
                    }
                }

                __instance.surplusLightActive = true;
                __instance.surplusLight.intensity = __instance.surplusLightIntensity;
                __instance.surplusLight.range = __instance.surplusLightRange;
            }

            RoundDirector.instance.extractionPointSurplus = 0;
            return false;
        }

        /// <summary>
        /// Splits surplus into random-sized bags.
        /// Only called when surplus > SingleBagMaxValue.
        ///
        /// Rules:
        ///   1. Bag count = ceil(surplus / AverageSplitBagValue), clamped to
        ///      [2, MaxBagsNormal] normally, [2, MaxBagsHuge] above HugeSurplusThreshold.
        ///   2. Each bag's base value = remaining / bagsLeft.
        ///   3. Random +/-30% variation is applied to each bag's value
        ///      UP TO (and including) HugeSurplusThreshold. Above the threshold
        ///      randomization is disabled and bags are distributed evenly.
        ///   4. Every bag is clamped to [MinSplitBagValue, softUpperBound].
        ///      softUpperBound normally equals SingleBagMaxValue, but grows
        ///      just enough to hold randomized upper outliers.
        ///   5. Last bag takes whatever remains.
        /// </summary>
        private static List<int> DistributeValues(int totalSurplus)
        {
            List<int> result = new List<int>();

            int bagCount = Mathf.CeilToInt((float)totalSurplus / AverageSplitBagValue);

            int maxBags = (totalSurplus > HugeSurplusThreshold)
                ? MaxBagsHuge
                : MaxBagsNormal;

            bagCount = Mathf.Clamp(bagCount, 2, maxBags);

            // SOFT UPPER BOUND: allow per-bag value to grow past
            // SingleBagMaxValue when the surplus is large, and reserve
            // headroom for the +ValueVariation upside so randomized bags
            // don't get clipped.
            int evenShare = Mathf.CeilToInt((float)totalSurplus / bagCount);
            int softUpperBound = Mathf.Max(
                SingleBagMaxValue,
                Mathf.CeilToInt(evenShare * (1f + ValueVariation))
            );

            bool useRandom = totalSurplus <= HugeSurplusThreshold;

            int remaining = totalSurplus;

            for (int i = 0; i < bagCount; i++)
            {
                int bagsLeft = bagCount - i;

                if (bagsLeft == 1)
                {
                    result.Add(Mathf.Max(remaining, MinSplitBagValue));
                    break;
                }

                int average = remaining / bagsLeft;

                int candidate;
                if (useRandom)
                {
                    int variation = Mathf.RoundToInt(
                        average * Random.Range(-ValueVariation, ValueVariation)
                    );
                    candidate = average + variation;
                }
                else
                {
                    candidate = average;
                }

                int minAllowed = MinSplitBagValue;
                int maxAllowed = Mathf.Min(
                    softUpperBound,
                    remaining - MinSplitBagValue * (bagsLeft - 1)
                );
                candidate = Mathf.Clamp(candidate, minAllowed, maxAllowed);

                result.Add(candidate);
                remaining -= candidate;
            }

            return result;
        }

        /// <summary>
        /// Spawns a single bag with the given value.
        /// </summary>
        private static void SpawnBag(ExtractionPoint instance, int value, int index, int totalBags, bool useCircleLayout)
        {
            // Base prefab by value.
            GameObject prefab;
            if (value > 8000)
                prefab = AssetManager.instance.surplusValuableBig;
            else if (value > 3500)
                prefab = AssetManager.instance.surplusValuableMedium;
            else
                prefab = AssetManager.instance.surplusValuableSmall;

            // Position.
            Vector3 spawnPos;
            if (useCircleLayout)
            {
                // Adaptive radius: with many bags, push them outward so they
                // don't pile up on top of each other and tank the physics.
                float adaptiveRadius = SpawnRadius + totalBags * 0.05f;

                float angle = (index / (float)totalBags) * Mathf.PI * 2f;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * adaptiveRadius,
                    0f,
                    Mathf.Sin(angle) * adaptiveRadius
                );
                spawnPos = instance.surplusSpawnTransform.position
                         + offset
                         + Vector3.up * (index * SpawnHeightStep);
            }
            else
            {
                spawnPos = instance.surplusSpawnTransform.position;
            }

            // Instantiate.
            GameObject spawned;
            if (!SemiFunc.IsMultiplayer())
                spawned = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
            else
                spawned = PhotonNetwork.InstantiateRoomObject(
                    "Valuables/" + prefab.name,
                    spawnPos,
                    Quaternion.identity,
                    0,
                    null
                );

            if (spawned == null) return;

            // Assign value.
            var valuable = spawned.GetComponent<ValuableObject>();
            if (valuable != null)
                valuable.dollarValueOverride = value;

            // Compute scale with THREE-segment mapping.
            float scale = ComputeScale(value);

            // Hard cap: never above MaxEnlargedScale even if the value is huge.
            scale = Mathf.Min(scale, MaxEnlargedScale);

            spawned.transform.localScale = new Vector3(scale, scale, scale);

            // Random spin.
            var phys = spawned.GetComponent<PhysGrabObject>();
            if (phys != null)
                phys.spawnTorque = Random.insideUnitSphere * 0.05f;
        }

        /// <summary>
        /// Three-segment scale curve:
        ///   [150$   .. 2000$]   -> [0.25 .. 0.55]   (visible tiny -> noticeable small)
        ///   [2000$  .. 10000$]  -> [0.55 .. 1.00]   (noticeable small -> vanilla big)
        ///   [10000$ .. 13000$]  -> [1.00 .. 1.30]   (vanilla big -> slightly enlarged)
        /// Values outside the range are clamped to the nearest end.
        /// </summary>
        private static float ComputeScale(int value)
        {
            if (value <= LowMidValue)
            {
                // Segment 1: tiny but visible bags.
                float t = Mathf.InverseLerp(ScaleMinValue, LowMidValue, value);
                return Mathf.Lerp(MinScale, LowMidScale, t);
            }
            else if (value <= ScaleMaxValue)
            {
                // Segment 2: small -> vanilla big.
                float t = Mathf.InverseLerp(LowMidValue, ScaleMaxValue, value);
                return Mathf.Lerp(LowMidScale, VanillaScale, t);
            }
            else
            {
                // Segment 3: vanilla big -> slightly enlarged.
                float t = Mathf.InverseLerp(ScaleMaxValue, SingleBagMaxValue, value);
                return Mathf.Lerp(VanillaScale, MaxEnlargedScale, t);
            }
        }
    }
}