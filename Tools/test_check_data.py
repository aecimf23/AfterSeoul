import unittest
import os
import subprocess
import sys
from unittest.mock import patch

from Tools import check_data


class RegionLadderTests(unittest.TestCase):
    def test_safer_later_region_is_not_dominated_by_riskier_route(self):
        fixture = {
            "balance.json": {"sellPriceRatio": 1, "baseWagePerHour": 100,
                             "equipment": {"luckCap": 0, "weaponLuckPerGrade": 0, "headsetLuckPerStep": 0}},
            "expeditions.json": {"expeditions": [
                {"mapId": "YONGSAN_MARKET", "unlockCondition": {"type": "explorationRoute"},
                 "lootTable": "RICH", "durationMinutes": 60, "baseCostWage": 0, "baseCostSupply": 0,
                 "riskLevel": 3, "combatChance": 0.3},
                {"mapId": "GURO_FACTORY", "unlockCondition": {"type": "explorationRoute"},
                 "lootTable": "SAFE", "durationMinutes": 60, "baseCostWage": 0, "baseCostSupply": 0,
                 "riskLevel": 1, "combatChance": 0.1},
            ]},
            "items.json": {"items": [{"id": "RICH_ITEM", "basePrice": 1000},
                                      {"id": "SAFE_ITEM", "basePrice": 100}]},
            "loot_tables.json": {"tables": [
                {"id": "RICH", "rolls": {"min": 1, "max": 1}, "entries": [
                    {"itemId": "RICH_ITEM", "weight": 1, "count": {"min": 1, "max": 1}}]},
                {"id": "SAFE", "rolls": {"min": 1, "max": 1}, "entries": [
                    {"itemId": "SAFE_ITEM", "weight": 1, "count": {"min": 1, "max": 1}}]},
            ]},
            "scav_pool.json": {"tiers": [{"tier": 1, "wagePerHour": 100}]},
        }
        check_data.problems.clear()
        with patch.object(check_data, "load", side_effect=fixture.__getitem__):
            check_data.check_region_ladder()

        self.assertEqual([], check_data.problems)

    def test_ammo_rounds_are_priced_as_units_in_route_comparison(self):
        fixture = {
            "balance.json": {"sellPriceRatio": 1, "baseWagePerHour": 100,
                             "equipment": {"luckCap": 0, "weaponLuckPerGrade": 0, "headsetLuckPerStep": 0}},
            "expeditions.json": {"expeditions": [
                {"mapId": "YONGSAN_MARKET", "unlockCondition": {"type": "explorationRoute"},
                 "lootTable": "AMMO", "durationMinutes": 60, "baseCostWage": 0, "baseCostSupply": 0},
                {"mapId": "GURO_FACTORY", "unlockCondition": {"type": "explorationRoute"},
                 "lootTable": "JUNK", "durationMinutes": 60, "baseCostWage": 0, "baseCostSupply": 0},
            ]},
            "items.json": {"items": [{"id": "AMO01", "category": "Ammo", "basePrice": 6000, "maxStack": 60},
                                      {"id": "JUNK01", "category": "Junk", "basePrice": 5000}]},
            "loot_tables.json": {"tables": [
                {"id": "AMMO", "rolls": {"min": 1, "max": 1}, "entries": [
                    {"itemId": "AMO01", "weight": 1, "count": {"min": 30, "max": 30}}]},
                {"id": "JUNK", "rolls": {"min": 1, "max": 1}, "entries": [
                    {"itemId": "JUNK01", "weight": 1, "count": {"min": 1, "max": 1}}]},
            ]},
            "scav_pool.json": {"tiers": [{"tier": 1, "wagePerHour": 100}]},
        }
        check_data.problems.clear()
        with patch.object(check_data, "load", side_effect=fixture.__getitem__):
            check_data.check_region_ladder()

        self.assertEqual([], check_data.problems)

    def test_console_reports_economy_failures_under_windows_encoding(self):
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "cp949"
        result = subprocess.run([sys.executable, check_data.__file__],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
        self.assertNotIn(b"Traceback", result.stderr)
        self.assertIn(result.returncode, (0, 1))

    def test_later_exploration_route_reports_dominated_payoff(self):
        fixture = {
            "balance.json": {
                "sellPriceRatio": 1,
                "baseWagePerHour": 100,
                "equipment": {"luckCap": 0, "weaponLuckPerGrade": 0, "headsetLuckPerStep": 0},
            },
            "expeditions.json": {"expeditions": [
                {"mapId": "YONGSAN_MARKET", "unlockCondition": {"type": "explorationRoute"},
                 "lootTable": "EARLY", "durationMinutes": 60, "baseCostWage": 0, "baseCostSupply": 0},
                {"mapId": "GURO_FACTORY", "unlockCondition": {"type": "explorationRoute"},
                 "lootTable": "LATE", "durationMinutes": 120, "baseCostWage": 0, "baseCostSupply": 0},
            ]},
            "items.json": {"items": [{"id": "EARLY_ITEM", "basePrice": 1000},
                                      {"id": "LATE_ITEM", "basePrice": 100}]},
            "loot_tables.json": {"tables": [
                {"id": "EARLY", "rolls": {"min": 1, "max": 1}, "entries": [
                    {"itemId": "EARLY_ITEM", "weight": 1, "count": {"min": 1, "max": 1}}]},
                {"id": "LATE", "rolls": {"min": 1, "max": 1}, "entries": [
                    {"itemId": "LATE_ITEM", "weight": 1, "count": {"min": 1, "max": 1}}]},
            ]},
            "scav_pool.json": {"tiers": [{"tier": 1, "wagePerHour": 100}]},
        }
        check_data.problems.clear()
        with patch.object(check_data, "load", side_effect=fixture.__getitem__):
            check_data.check_region_ladder()

        self.assertTrue(any("GURO_FACTORY" in p and "YONGSAN_MARKET" in p
                            for p in check_data.problems), check_data.problems)


if __name__ == "__main__":
    unittest.main()
