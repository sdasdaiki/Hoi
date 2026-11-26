using System;
using System.Collections.Generic;
using EdoStrategy.Map;
using EdoStrategy.Units;

namespace EdoStrategy.Combat
{
    [Serializable]
    public class CombatResult
    {
        public string attackerUnitId;
        public string defenderUnitId;
        public int effectiveAttackPower;
        public int effectiveBreakthrough;
        public int effectiveDefensePower;
        public int netDefensePower;
        public List<UnitModifier> appliedModifiers = new List<UnitModifier>();
        public int defenderManpowerDamage;
        public int defenderOrganizationDamage;
        public bool counterAttackOccurred;
        public int attackerManpowerDamage;
        public int attackerOrganizationDamage;
    }

    public static class CombatResolver
    {
        private const int Permille = 1000;
        private const int MinimumNetDefense = 50;

        public static bool CanAttack(
            UnitData attacker,
            UnitData defender,
            UnitDefinition attackerDef)
        {
            return attacker != null &&
                defender != null &&
                attacker.CurrentTotalManpower > 0 &&
                attacker.organization > 0 &&
                defender.CurrentTotalManpower > 0 &&
                defender.organization > 0 &&
                attacker.faction != defender.faction &&
                attackerDef != null &&
                attackerDef.attackRange > 0 &&
                HexUtility.DistanceOffset(
                    attacker.x,
                    attacker.y,
                    defender.x,
                    defender.y) <= attackerDef.attackRange;
        }

        public static UnitModifier EvaluateFlankingModifier(
            UnitData defender,
            HexGrid grid,
            UnitManager unitManager)
        {
            if (defender == null || grid == null || unitManager == null)
            {
                return null;
            }

            var enemyDirections = new bool[6];
            int enemyDirectionCount = 0;
            int escapeRoutes = 0;

            for (int direction = 0; direction < 6; direction++)
            {
                UnityEngine.Vector2Int neighbor =
                    HexUtility.GetNeighborOffset(defender.x, defender.y, direction);
                if (!grid.IsInBounds(neighbor.x, neighbor.y))
                {
                    continue;
                }

                List<UnitData> occupants =
                    unitManager.GetUnitsAt(neighbor.x, neighbor.y);
                bool hasEnemy = false;
                for (int i = 0; i < occupants.Count; i++)
                {
                    UnitData occupant = occupants[i];
                    if (occupant != null &&
                        occupant.IsAlive &&
                        occupant.faction != defender.faction)
                    {
                        hasEnemy = true;
                        break;
                    }
                }

                if (hasEnemy)
                {
                    enemyDirections[direction] = true;
                    enemyDirectionCount++;
                }
                else if (grid.GetMoveCost(neighbor.x, neighbor.y) >= 0)
                {
                    escapeRoutes++;
                }
            }

            if (enemyDirectionCount < 2)
            {
                return null;
            }

            if (escapeRoutes == 0)
            {
                return CreateModifier(
                    "encircled",
                    "Encircled",
                    defenseMultiplier: 500,
                    orgDamageMultiplier: 1600);
            }

            bool hasPincer = false;
            bool hasCrossfire = false;
            for (int first = 0; first < 6; first++)
            {
                if (!enemyDirections[first])
                {
                    continue;
                }

                for (int second = first + 1; second < 6; second++)
                {
                    if (!enemyDirections[second])
                    {
                        continue;
                    }

                    int difference = second - first;
                    if (difference == 3)
                    {
                        hasPincer = true;
                    }
                    else if (difference == 2 || difference == 4)
                    {
                        hasCrossfire = true;
                    }
                }
            }

            if (hasPincer)
            {
                return CreateModifier(
                    "pincer",
                    "Pincer",
                    defenseMultiplier: 650,
                    orgDamageMultiplier: 1500);
            }

            if (hasCrossfire)
            {
                return CreateModifier(
                    "crossfire",
                    "Crossfire",
                    defenseMultiplier: 750,
                    orgDamageMultiplier: 1300);
            }

            return CreateModifier(
                "flanked",
                "Flanked",
                defenseMultiplier: 850,
                orgDamageMultiplier: 1150);
        }

        public static UnitModifier EvaluateOverstackingModifier(
            UnitData unit,
            bool isDefending,
            UnitManager unitManager,
            Func<string, UnitDefinition> getUnitDef)
        {
            if (unit == null || unitManager == null)
            {
                return null;
            }

            List<UnitData> occupants = unitManager.GetUnitsAt(unit.x, unit.y);
            int stackCount = 0;
            bool hasCommandUnit = false;

            for (int i = 0; i < occupants.Count; i++)
            {
                UnitData occupant = occupants[i];
                if (occupant == null ||
                    !occupant.IsAlive ||
                    occupant.faction != unit.faction)
                {
                    continue;
                }

                stackCount++;
                if (HasCommandUnit(occupant, getUnitDef))
                {
                    hasCommandUnit = true;
                }
            }

            int limit = hasCommandUnit ? 3 : 2;
            int excess = stackCount - limit;
            if (excess < 1)
            {
                return null;
            }

            int penalty = (int)Math.Max(500L, 1000L - (long)excess * 100L);
            if (isDefending)
            {
                return CreateModifier(
                    "overstacking_defense",
                    "Overstacking",
                    defenseMultiplier: penalty,
                    orgDamageMultiplier: 1000 + (int)Math.Min(
                        int.MaxValue - 1000L,
                        (long)excess * 100L));
            }

            return CreateModifier(
                "overstacking_attack",
                "Overstacking",
                attackMultiplier: penalty);
        }

        public static UnitModifier EvaluateAttackingExposureModifier(
            bool isDefenderCurrentlyAttacking)
        {
            if (!isDefenderCurrentlyAttacking)
            {
                return null;
            }

            return CreateModifier(
                "attacking_exposure",
                "Attacking Exposure",
                defenseMultiplier: 800,
                orgDamageMultiplier: 1200);
        }

        public static UnitModifier EvaluateAssaultAttackerModifier()
        {
            return CreateModifier(
                "assault_attack",
                "Assault",
                attackMultiplier: 850,
                defenseMultiplier: 850);
        }

        public static UnitModifier EvaluateAssaultDefenderModifier()
        {
            return CreateModifier(
                "assault_defense",
                "Assaulted",
                defenseMultiplier: 850,
                orgDamageMultiplier: 1150);
        }

        public static UnitModifier EvaluateEntrenchModifier()
        {
            return CreateModifier(
                "entrenching",
                "Entrenching",
                defenseMultiplier: 750,
                orgDamageMultiplier: 1250);
        }

        public static UnitModifier EvaluateAmbushAttackerModifier()
        {
            return CreateModifier(
                "ambush_attack",
                "Ambush",
                attackMultiplier: 1500);
        }

        public static UnitModifier EvaluateAmbushDefenderModifier()
        {
            return CreateModifier(
                "ambush_defense",
                "Ambushed",
                defenseMultiplier: 750,
                orgDamageMultiplier: 1250);
        }

        public static CombatResult ResolveCombat(
            UnitData attacker,
            UnitData defender,
            UnitDefinition attackerDef,
            UnitDefinition defenderDef,
            HexGrid grid,
            UnitManager unitManager,
            Func<string, UnitDefinition> getUnitDef,
            bool isDefenderCurrentlyAttacking)
        {
            return ResolveCombat(
                attacker,
                defender,
                attackerDef,
                defenderDef,
                grid,
                unitManager,
                getUnitDef,
                isDefenderCurrentlyAttacking,
                null,
                null,
                Permille,
                true,
                false);
        }

        public static CombatResult ResolveCombat(
            UnitData attacker,
            UnitData defender,
            UnitDefinition attackerDef,
            UnitDefinition defenderDef,
            HexGrid grid,
            UnitManager unitManager,
            Func<string, UnitDefinition> getUnitDef,
            bool isDefenderCurrentlyAttacking,
            UnitModifier attackerOrderModifier,
            UnitModifier defenderOrderModifier,
            int damageMultiplier,
            bool allowCounterAttack,
            bool ignoreRange)
        {
            var result = new CombatResult
            {
                attackerUnitId = attacker == null ? null : attacker.unitId,
                defenderUnitId = defender == null ? null : defender.unitId
            };

            if (!ignoreRange && !CanAttack(attacker, defender, attackerDef))
            {
                return result;
            }
            if (attacker == null ||
                defender == null ||
                attacker.CurrentTotalManpower <= 0 ||
                attacker.organization <= 0 ||
                defender.CurrentTotalManpower <= 0 ||
                defender.organization <= 0 ||
                attacker.faction == defender.faction ||
                attackerDef == null)
            {
                return result;
            }

            UnitModifier attackerStackModifier = EvaluateOverstackingModifier(
                attacker,
                false,
                unitManager,
                getUnitDef);
            UnitModifier defenderStackModifier = EvaluateOverstackingModifier(
                defender,
                true,
                unitManager,
                getUnitDef);
            UnitModifier flankingModifier =
                EvaluateFlankingModifier(defender, grid, unitManager);
            UnitModifier exposureModifier =
                EvaluateAttackingExposureModifier(isDefenderCurrentlyAttacking);

            AddModifier(result.appliedModifiers, attackerStackModifier);
            AddModifier(result.appliedModifiers, defenderStackModifier);
            AddModifier(result.appliedModifiers, flankingModifier);
            AddModifier(result.appliedModifiers, exposureModifier);
            AddModifier(result.appliedModifiers, attackerOrderModifier);
            AddModifier(result.appliedModifiers, defenderOrderModifier);

            int terrainDefenseMultiplier =
                GetTerrainDefenseMultiplier(grid, defender.x, defender.y);
            if (terrainDefenseMultiplier != Permille)
            {
                AddModifier(
                    result.appliedModifiers,
                    CreateModifier(
                        "terrain_defense",
                        "Terrain Defense",
                        defenseMultiplier: terrainDefenseMultiplier));
            }

            int attackPower = CalculateEffectiveAttack(
                attacker,
                attackerDef,
                getUnitDef,
                attackerStackModifier);
            attackPower = ScaleByPermille(
                attackPower,
                attackerOrderModifier == null
                    ? Permille
                    : Math.Max(0, attackerOrderModifier.attackMultiplier));
            int breakthrough = CalculateEffectiveBreakthrough(
                attacker,
                attackerDef,
                getUnitDef);
            int defensePower = CalculateEffectiveDefense(
                defender,
                defenderDef,
                getUnitDef,
                terrainDefenseMultiplier,
                defenderStackModifier,
                flankingModifier,
                exposureModifier);
            defensePower = ScaleByPermille(
                defensePower,
                defenderOrderModifier == null
                    ? Permille
                    : Math.Max(0, defenderOrderModifier.defenseMultiplier));
            int netDefense = CalculateNetDefense(defensePower, breakthrough);

            result.effectiveAttackPower = attackPower;
            result.effectiveBreakthrough = breakthrough;
            result.effectiveDefensePower = defensePower;
            result.netDefensePower = netDefense;

            int manpowerDamage = CalculateManpowerDamage(attackPower, netDefense);
            int organizationDamage = CalculateOrganizationDamage(
                attackPower,
                netDefense,
                GetOrganizationDamageMultiplier(
                    defenderStackModifier,
                    flankingModifier,
                    exposureModifier,
                    defenderOrderModifier));
            manpowerDamage = ScaleDamageByPermille(
                manpowerDamage,
                damageMultiplier);
            organizationDamage = ScaleDamageByPermille(
                organizationDamage,
                damageMultiplier);

            result.defenderManpowerDamage =
                ApplyManpowerDamage(defender, manpowerDamage);
            result.defenderOrganizationDamage =
                ApplyOrganizationDamage(defender, organizationDamage);

            UnitDefinition counterDefinition = GetUnitDefinition(
                defender,
                defenderDef,
                getUnitDef);
            int distance = HexUtility.DistanceOffset(
                attacker.x,
                attacker.y,
                defender.x,
                defender.y);
            int counterAttackRange = GetUnitDefinitionAttackRange(
                defender,
                defenderDef,
                getUnitDef);
            if (!allowCounterAttack ||
                defender.CurrentTotalManpower <= 0 ||
                defender.organization <= 0 ||
                counterDefinition == null ||
                counterAttackRange <= 0 ||
                counterAttackRange < distance)
            {
                return result;
            }

            result.counterAttackOccurred = true;
            UnitModifier counterAttackStackModifier =
                EvaluateOverstackingModifier(
                    defender,
                    false,
                    unitManager,
                    getUnitDef);
            UnitModifier counterDefenseStackModifier =
                EvaluateOverstackingModifier(
                    attacker,
                    true,
                    unitManager,
                    getUnitDef);
            UnitModifier counterFlankingModifier =
                EvaluateFlankingModifier(attacker, grid, unitManager);
            UnitModifier counterExposureModifier =
                EvaluateAttackingExposureModifier(true);

            AddModifier(result.appliedModifiers, counterAttackStackModifier);
            AddModifier(result.appliedModifiers, counterDefenseStackModifier);
            AddModifier(result.appliedModifiers, counterFlankingModifier);
            AddModifier(result.appliedModifiers, counterExposureModifier);

            int counterTerrainMultiplier =
                GetTerrainDefenseMultiplier(grid, attacker.x, attacker.y);
            if (counterTerrainMultiplier != Permille)
            {
                AddModifier(
                    result.appliedModifiers,
                    CreateModifier(
                        "counter_terrain_defense",
                        "Counterattack Terrain Defense",
                        defenseMultiplier: counterTerrainMultiplier));
            }

            int counterAttackPower = CalculateEffectiveAttack(
                defender,
                counterDefinition,
                getUnitDef,
                counterAttackStackModifier);
            counterAttackPower = ScaleByPermille(
                counterAttackPower,
                defenderOrderModifier == null
                    ? Permille
                    : Math.Max(0, defenderOrderModifier.attackMultiplier));
            int counterBreakthrough = CalculateEffectiveBreakthrough(
                defender,
                counterDefinition,
                getUnitDef);
            int counterDefensePower = CalculateEffectiveDefense(
                attacker,
                attackerDef,
                getUnitDef,
                counterTerrainMultiplier,
                counterDefenseStackModifier,
                counterFlankingModifier,
                counterExposureModifier);
            counterDefensePower = ScaleByPermille(
                counterDefensePower,
                attackerOrderModifier == null
                    ? Permille
                    : Math.Max(0, attackerOrderModifier.defenseMultiplier));
            int counterNetDefense =
                CalculateNetDefense(counterDefensePower, counterBreakthrough);

            int counterManpowerDamage = ScaleDamageByPermille(
                CalculateManpowerDamage(counterAttackPower, counterNetDefense),
                damageMultiplier);
            int counterOrganizationDamage = ScaleDamageByPermille(
                CalculateOrganizationDamage(
                    counterAttackPower,
                    counterNetDefense,
                    GetOrganizationDamageMultiplier(
                        counterDefenseStackModifier,
                        counterFlankingModifier,
                        counterExposureModifier,
                        attackerOrderModifier)),
                damageMultiplier);
            result.attackerManpowerDamage = ApplyManpowerDamage(
                attacker,
                counterManpowerDamage);
            result.attackerOrganizationDamage = ApplyOrganizationDamage(
                attacker,
                counterOrganizationDamage);

            return result;
        }

        private static bool HasCommandUnit(
            UnitData unit,
            Func<string, UnitDefinition> getUnitDef)
        {
            if (unit.battalions == null)
            {
                return false;
            }

            for (int i = 0; i < unit.battalions.Count; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion == null || battalion.currentManpower <= 0)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(
                    battalion,
                    null,
                    getUnitDef);
                if (definition != null && definition.commandLevel >= 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static UnitDefinition GetUnitDefinition(
            UnitData unit,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef)
        {
            if (unit == null || unit.battalions == null)
            {
                return fallback;
            }

            for (int i = 0; i < unit.battalions.Count; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion == null || battalion.currentManpower <= 0)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(
                    battalion,
                    null,
                    getUnitDef);
                if (definition != null)
                {
                    return definition;
                }
            }

            return fallback;
        }

        private static UnitDefinition GetUnitDefinition(
            BattalionData battalion,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef)
        {
            if (battalion != null &&
                getUnitDef != null &&
                !string.IsNullOrEmpty(battalion.unitTypeId))
            {
                UnitDefinition definition = getUnitDef(battalion.unitTypeId);
                if (definition != null)
                {
                    return definition;
                }
            }

            return fallback;
        }

        private static int CalculateEffectiveAttack(
            UnitData unit,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef,
            UnitModifier attackModifier)
        {
            int power = CalculateBasePower(
                unit,
                fallback,
                getUnitDef,
                definition => definition.baseAttack,
                includeEquipment: true);
            power = ScaleByPermille(power, Math.Max(0, unit.supplyStatus));
            if (attackModifier != null)
            {
                power = ScaleByPermille(
                    power,
                    Math.Max(0, attackModifier.attackMultiplier));
            }
            return power;
        }

        private static int CalculateEffectiveBreakthrough(
            UnitData unit,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef)
        {
            return CalculateBasePower(
                unit,
                fallback,
                getUnitDef,
                definition => definition.breakthrough,
                includeEquipment: true);
        }

        private static int CalculateEffectiveDefense(
            UnitData unit,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef,
            int terrainMultiplier,
            UnitModifier stackModifier,
            UnitModifier flankingModifier,
            UnitModifier exposureModifier)
        {
            int defense = CalculateBasePower(
                unit,
                fallback,
                getUnitDef,
                definition => definition.baseDefense,
                includeEquipment: false);
            defense = ScaleByPermille(
                defense,
                Math.Max(0, terrainMultiplier));
            defense = ScaleByPermille(
                defense,
                stackModifier == null
                    ? Permille
                    : Math.Max(0, stackModifier.defenseMultiplier));
            defense = ScaleByPermille(
                defense,
                flankingModifier == null
                    ? Permille
                    : Math.Max(0, flankingModifier.defenseMultiplier));
            defense = ScaleByPermille(
                defense,
                exposureModifier == null
                    ? Permille
                    : Math.Max(0, exposureModifier.defenseMultiplier));
            return defense;
        }

        private static int CalculateBasePower(
            UnitData unit,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef,
            Func<UnitDefinition, int> getStat,
            bool includeEquipment)
        {
            if (unit == null || unit.battalions == null || getStat == null)
            {
                return 0;
            }

            long total = 0;
            for (int i = 0; i < unit.battalions.Count; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion == null ||
                    battalion.currentManpower <= 0 ||
                    battalion.maxManpower <= 0)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(
                    battalion,
                    fallback,
                    getUnitDef);
                if (definition == null)
                {
                    continue;
                }

                int baseStat = Math.Max(0, getStat(definition));
                int equipmentRatio = includeEquipment
                    ? Math.Max(0, battalion.equipmentRatio)
                    : Permille;
                int contribution = ScaleRatioByPermilleToInt(
                    (long)baseStat * battalion.currentManpower,
                    battalion.maxManpower,
                    equipmentRatio);

                total += contribution;
                if (total >= int.MaxValue)
                {
                    return int.MaxValue;
                }
            }

            return (int)total;
        }

        private static int GetTerrainDefenseMultiplier(
            HexGrid grid,
            int x,
            int y)
        {
            if (grid == null)
            {
                return Permille;
            }

            HexCell cell = grid.GetCell(x, y);
            TerrainDefinition terrain = cell == null
                ? null
                : grid.GetTerrainDefinition(cell.terrainId);
            int terrainMultiplier = terrain == null
                ? Permille
                : Math.Max(0, terrain.defenseMultiplier);
            int fortificationLevel = cell == null
                ? 0
                : Math.Max(0, Math.Min(5, cell.fortificationLevel));
            return ScaleByPermille(
                terrainMultiplier,
                Permille + fortificationLevel * 100);
        }

        private static int CalculateNetDefense(int defense, int breakthrough)
        {
            long reducedDefense = (long)defense - (long)breakthrough * 300L / Permille;
            return (int)Math.Max(MinimumNetDefense, reducedDefense);
        }

        private static int CalculateManpowerDamage(int attack, int netDefense)
        {
            long denominator = ((long)attack + netDefense) * 2L;
            long damage = denominator <= 0
                ? 0
                : (long)attack * attack / denominator;
            return Math.Max(10, ClampToInt(damage));
        }

        private static int CalculateOrganizationDamage(
            int attack,
            int netDefense,
            int orgDamageMultiplier)
        {
            int damage = ScaleRatioByPermilleToInt(
                (long)attack * 300L,
                (long)netDefense + 200L,
                Math.Max(0, orgDamageMultiplier));
            return Math.Max(15, damage);
        }

        private static int GetOrganizationDamageMultiplier(
            UnitModifier stackModifier,
            UnitModifier flankingModifier,
            UnitModifier exposureModifier,
            UnitModifier orderModifier)
        {
            int multiplier = Permille;
            multiplier = ScaleByPermille(
                multiplier,
                stackModifier == null
                    ? Permille
                    : Math.Max(0, stackModifier.orgDamageMultiplier));
            multiplier = ScaleByPermille(
                multiplier,
                flankingModifier == null
                    ? Permille
                    : Math.Max(0, flankingModifier.orgDamageMultiplier));
            multiplier = ScaleByPermille(
                multiplier,
                exposureModifier == null
                    ? Permille
                    : Math.Max(0, exposureModifier.orgDamageMultiplier));
            multiplier = ScaleByPermille(
                multiplier,
                orderModifier == null
                    ? Permille
                    : Math.Max(0, orderModifier.orgDamageMultiplier));
            return multiplier;
        }

        private static int ApplyManpowerDamage(UnitData unit, int requestedDamage)
        {
            if (unit == null ||
                unit.battalions == null ||
                requestedDamage <= 0)
            {
                return 0;
            }

            long totalManpower = 0;
            for (int i = 0; i < unit.battalions.Count; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion != null && battalion.currentManpower > 0)
                {
                    totalManpower += battalion.currentManpower;
                }
            }

            int appliedDamage = (int)Math.Min(totalManpower, requestedDamage);
            if (appliedDamage <= 0 || totalManpower <= 0)
            {
                return 0;
            }

            var allocations = new int[unit.battalions.Count];
            int allocated = 0;
            for (int i = 0; i < unit.battalions.Count; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion == null || battalion.currentManpower <= 0)
                {
                    continue;
                }

                allocations[i] = (int)((long)appliedDamage *
                    battalion.currentManpower / totalManpower);
                allocated += allocations[i];
            }

            allocations[0] += appliedDamage - allocated;
            int overflow = 0;
            for (int i = 0; i < allocations.Length; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion == null)
                {
                    continue;
                }

                if (allocations[i] > battalion.currentManpower)
                {
                    overflow += allocations[i] - Math.Max(0, battalion.currentManpower);
                    allocations[i] = Math.Max(0, battalion.currentManpower);
                }
            }

            for (int i = 0; i < allocations.Length && overflow > 0; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion == null)
                {
                    continue;
                }

                int available = Math.Max(0, battalion.currentManpower - allocations[i]);
                int added = Math.Min(available, overflow);
                allocations[i] += added;
                overflow -= added;
            }

            for (int i = 0; i < allocations.Length; i++)
            {
                BattalionData battalion = unit.battalions[i];
                if (battalion != null)
                {
                    battalion.currentManpower =
                        Math.Max(0, battalion.currentManpower - allocations[i]);
                }
            }

            return appliedDamage - overflow;
        }

        private static int ApplyOrganizationDamage(UnitData unit, int requestedDamage)
        {
            if (unit == null || requestedDamage <= 0 || unit.organization <= 0)
            {
                return 0;
            }

            int appliedDamage = Math.Min(unit.organization, requestedDamage);
            unit.organization = Math.Max(0, unit.organization - appliedDamage);
            return appliedDamage;
        }

        private static int GetUnitDefinitionAttackRange(
            UnitData unit,
            UnitDefinition fallback,
            Func<string, UnitDefinition> getUnitDef)
        {
            int attackRange = fallback == null ? 0 : fallback.attackRange;
            if (unit == null || unit.battalions == null)
            {
                return attackRange;
            }

            for (int i = 0; i < unit.battalions.Count; i++)
            {
                UnitDefinition definition = GetUnitDefinition(
                    unit.battalions[i],
                    null,
                    getUnitDef);
                if (definition != null)
                {
                    attackRange = Math.Max(attackRange, definition.attackRange);
                }
            }

            return attackRange;
        }

        private static int ScaleByPermille(int value, int multiplier)
        {
            if (value <= 0 || multiplier <= 0)
            {
                return 0;
            }

            return ClampToInt((long)value * multiplier / Permille);
        }

        private static int ScaleDamageByPermille(int damage, int multiplier)
        {
            if (damage <= 0 || multiplier <= 0)
            {
                return 0;
            }

            return Math.Max(1, ScaleByPermille(damage, multiplier));
        }

        private static int ScaleRatioByPermilleToInt(
            long numerator,
            long denominator,
            int multiplier)
        {
            if (numerator <= 0 || denominator <= 0 || multiplier <= 0)
            {
                return 0;
            }

            long quotient = numerator / denominator;
            long maximumQuotient = (long)int.MaxValue * Permille / multiplier;
            if (quotient > maximumQuotient)
            {
                return int.MaxValue;
            }

            long remainder = numerator % denominator;
            long scaled = quotient * multiplier +
                remainder * multiplier / denominator;
            return ClampToInt(scaled / Permille);
        }

        private static int ClampToInt(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static void AddModifier(
            List<UnitModifier> modifiers,
            UnitModifier modifier)
        {
            if (modifier != null)
            {
                modifiers.Add(modifier);
            }
        }

        private static UnitModifier CreateModifier(
            string sourceId,
            string displayName,
            int attackMultiplier = Permille,
            int defenseMultiplier = Permille,
            int orgDamageMultiplier = Permille)
        {
            return new UnitModifier
            {
                sourceId = sourceId,
                displayName = displayName,
                attackMultiplier = attackMultiplier,
                defenseMultiplier = defenseMultiplier,
                orgDamageMultiplier = orgDamageMultiplier
            };
        }
    }
}
