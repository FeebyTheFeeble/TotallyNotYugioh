# Registered Card Behavior Test Report

Generated: 2026-11-10 21:13:27

A card is considered working only when it has an explicitly associated card-specific behavior test and all associated tests pass. Imported text and effect mappings do not count as behavior coverage. Cards with imported effect mappings but without a passing behavior test are reported as FAIL.

## Full-catalog implementation coverage

Every unique catalog card is counted once. Vanilla normal monsters and Token definitions with no mapped effect count as implemented no-effect cards; effect cards count as implemented only when their mapped mechanic has passing card-attributed behavior tests. Everything else, including unmapped cards with text effects, remains unimplemented.

| Coverage | Cards |
|---|---:|
| Total unique catalog cards | 14137 |
| Implemented (including no-effect cards) | 857 |
| Unimplemented | 13280 |
| Implemented no-effect / vanilla cards | 763 |
| Implemented cards with tested effects | 94 |

| Implemented card category | Cards |
|---|---:|
| Normal / Token cards with no effect | 763 |
| Other tested effects | 19 |
| Special Summon / revive monsters | 16 |
| Modify stats / battle behavior | 13 |
| Draw cards / card advantage | 9 |
| Negate effects / stop attacks | 9 |
| Remove or disable a targeted card | 9 |
| Destroy multiple cards | 8 |
| Create Tokens / summon from Deck | 4 |
| Search the Deck and add to hand | 4 |
| Banish an attacking monster | 1 |
| Change Life Points | 1 |
| Return or move cards | 1 |

| Unimplemented card category | Cards |
|---|---:|
| Other unimplemented cards | 12799 |
| Mass-destruction effects | 421 |
| Alternate-win-condition effects (Exodia, FINAL, etc.) | 60 |

## Summary

| Result | Count |
|---|---:|
| CSV records imported | 14137 |
| Imported cards passing identity/data validation | 14137 |
| Unique cards in the four structure decks | 68 |
| Structure-deck cards with passing behavior tests | 68 |
| Structure-deck cards failing behavior coverage | 0 |
| Structure-deck-specific behavior tests | 126 |
| Passing structure-deck-specific behavior tests | 126 |
| Cards with imported effect mappings | 94 |
| Mapped cards with passing behavior tests | 94 |
| Mapped cards failing behavior coverage | 0 |
| Non-structure mapped cards with passing behavior tests | 26 |
| Non-structure mapped cards failing behavior coverage | 0 |
| Non-structure card-specific behavior tests | 29 |
| Mock-duel scenarios passed | 10 |
| Card/effect report tests passed | 160 |
| Card/effect report tests failed | 0 |

The structure-deck table tracks all 68 cards in the four configured decks. The additional-card table tracks every catalog card with an imported effect mapping outside those decks. Other CSV cards with no imported effect mapping are not yet considered implemented or covered.

## EditMode test results

| Result | Test | Details |
|---|---|---|
| PASSED | Ai_attack_emits_the_same_attack_event_as_a_human_attack |  |
| PASSED | Ai_turn_steps_advance_only_one_phase_command_at_a_time |  |
| PASSED | Ai_uses_Harpies_Feather_Duster_when_destroying_four_cards_is_profitable |  |
| PASSED | Ancient_Fairy_Dragon_does_not_draw_on_Synchro_Summon_and_can_activate_both_Main_Phase_effects |  |
| PASSED | Ancient_Fairy_Dragon_is_not_mapped_to_Life_Stream_Dragon_effect |  |
| PASSED | Black_Rose_Dragon_destroys_all_field_cards_when_Synchro_Summoned |  |
| PASSED | Black_Whirlwind_searches_a_lower_ATK_Blackwing_after_a_Normal_Summon |  |
| PASSED | Blackwing_Armed_Wing_gains_ATK_and_pierces_Defense_Position |  |
| PASSED | Blackwing_Armor_Master_survives_battle_and_applies_Wedge_Counters |  |
| PASSED | Blackwing_Blizzard_revives_a_low_Level_Blackwing_in_Defense_Position |  |
| PASSED | Blackwing_Bora_can_special_summon_while_another_Blackwing_is_controlled |  |
| PASSED | Blackwing_Gale_halves_a_face_up_opposing_monsters_attack |  |
| PASSED | Blackwing_Kalut_boosts_a_Blackwing_that_has_declared_an_attack |  |
| PASSED | Blackwing_Mistral_prevents_the_next_battle_damage_after_it_is_destroyed |  |
| PASSED | Blackwing_Shura_summons_a_low_attack_Blackwing_after_battle |  |
| PASSED | Blackwing_Silverwind_destroys_up_to_two_lower_DEF_face_up_monsters |  |
| PASSED | Blackwing_Sirocco_boosts_a_selected_Blackwing_by_other_Blackwings_attack |  |
| PASSED | Blackwing_Vayu_banishes_itself_and_a_Blackwing_to_summon_a_negated_Synchro |  |
| PASSED | Blackwing_Zephyros_returns_a_face_up_card_and_special_summons_once_per_Duel |  |
| PASSED | Book_of_Moon_sets_a_face_up_monster_to_face_down_defense |  |
| PASSED | Bottomless_Trap_Hole_destroys_and_banishes_a_1500_ATK_summon |  |
| PASSED | Call_of_the_Haunted_revives_a_monster_in_Attack_Position_and_remains_equipped |  |
| PASSED | Card_Destruction_discards_the_remaining_hand_then_draws_the_same_count |  |
| PASSED | Card_draw_event_reports_the_player_and_drawn_card |  |
| PASSED | Cards_default_to_three_deck_copies_and_can_configure_a_lower_limit |  |
| PASSED | Clear_Effector_draws_when_used_as_Synchro_material_and_protects_the_Synchro_from_effect_destruction |  |
| PASSED | Colossal_Fighter_gains_ATK_for_Warriors_in_both_Graveyards |  |
| PASSED | Colossal_Fighter_revives_a_Warrior_when_destroyed_by_battle |  |
| PASSED | Compulsory_Evacuation_Device_returns_the_target_to_its_owners_hand |  |
| PASSED | Csv_catalog_cards_have_unique_ids_and_preserved_card_text |  |
| PASSED | Csv_parser_preserves_escaped_quotes_commas_and_multiline_effect_text |  |
| PASSED | Cyber_Dragon_can_special_summon_if_the_opponent_controls_more_monsters |  |
| PASSED | Cyber_Jar_destroys_monsters_and_processes_up_to_five_cards_from_each_deck |  |
| PASSED | Cyber_Jar_flip_effect_resolves_during_battle_before_damage |  |
| PASSED | Dandylion_creates_two_tokens_that_can_be_Tributed_after_their_summon_turn |  |
| PASSED | Dark_Core_discards_a_card_to_banish_a_face_up_monster |  |
| PASSED | Dark_Hole_destroys_monsters_on_both_sides_of_the_field |  |
| PASSED | Debris_Dragon_revives_and_negates_a_low_ATK_monster_on_Normal_Summon |  |
| PASSED | Dekoichi_draws_when_flip_summoned |  |
| PASSED | Delta_Crow_destroys_all_Set_opposing_Spells_and_Traps_with_a_Blackwing |  |
| PASSED | Dimensional_Prison_banishes_the_attacking_monster |  |
| PASSED | Direct_attack_animation_targets_the_opposite_players_side |  |
| PASSED | Divine_Wrath_discards_to_negate_and_destroy_a_monster_effect_source |  |
| PASSED | Doppelwarrior_creates_two_tokens_when_used_as_Synchro_material |  |
| PASSED | Doppelwarrior_special_summons_from_hand_after_a_monster_is_revived |  |
| PASSED | Drill_Warrior_halves_its_ATK_and_can_attack_directly |  |
| PASSED | Drill_Warrior_returns_on_the_next_Standby_Phase_and_recovers_a_monster |  |
| PASSED | Duel_engine_rejects_either_deck_when_it_has_fewer_than_40_cards |  |
| PASSED | Duel_state_json_rejects_cards_referenced_by_multiple_zones |  |
| PASSED | Duel_state_json_rejects_unsupported_schema_versions |  |
| PASSED | Duel_state_json_round_trip_preserves_zones_and_card_state |  |
| PASSED | Dust_Tornado_destroys_only_the_selected_opposing_Spell_or_Trap |  |
| PASSED | Example_duel_state_json_files_load("opponent-battle-phase.json",Battle) |  |
| PASSED | Example_duel_state_json_files_load("starter-main-phase.json",MainOne) |  |
| PASSED | Foolish_Burial_sends_a_selected_monster_from_the_Deck_to_the_Graveyard |  |
| PASSED | Formula_Synchron_can_Quick_Synchro_Summon_during_the_opponents_Main_Phase_using_itself_and_still_draw |  |
| PASSED | Formula_Synchron_draws_when_Synchro_Summoned |  |
| PASSED | Giant_Trunade_returns_Spell_and_Trap_cards_to_their_owners_hands |  |
| PASSED | Goyo_Guardian_takes_control_of_a_monster_it_destroys_by_battle |  |
| PASSED | Graceful_Charity_draws_three_then_discards_two_other_cards |  |
| PASSED | Hand_visual_positions_match_the_rendered_player_and_opponent_hands |  |
| PASSED | Harpies_Feather_Duster_destroys_only_the_opponents_Spells_and_Traps |  |
| PASSED | Heavy_Storm_destroys_Spells_and_Traps_on_both_sides |  |
| PASSED | Human_attack_emits_one_attack_event |  |
| PASSED | Human_can_end_the_turn_from_an_earlier_phase |  |
| PASSED | Icarus_Attack_tributes_a_Winged_Beast_and_destroys_two_field_cards |  |
| PASSED | Jar_of_Greed_uses_the_shared_draw_action_when_its_Set_Trap_is_activated |  |
| PASSED | Jinzo_does_not_block_Trap_activation_when_its_effect_is_negated |  |
| PASSED | Jinzo_prevents_Set_Trap_Cards_from_being_activated |  |
| PASSED | Junk_Converter_revives_a_Tuner_when_used_as_Synchro_material |  |
| PASSED | Junk_Synchron_revives_a_Level_two_or_lower_monster_on_Normal_Summon |  |
| PASSED | Junk_Warrior_gains_ATK_from_other_low_Level_monsters_after_Synchro_Summoning |  |
| PASSED | Level_Eater_reduces_a_high_Level_monster_and_special_summons_itself |  |
| PASSED | Life_Stream_Dragon_sets_LP_only_when_Synchro_Summoned_and_banishes_an_Equip_to_prevent_destruction |  |
| PASSED | Life_Stream_Dragon_sets_Life_Points_and_prevents_Zephyros_effect_damage |  |
| PASSED | Lightning_Vortex_discards_one_card_and_destroys_only_face_up_opponent_monsters |  |
| PASSED | Limiter_Removal_doubles_Machine_ATK_then_destroys_the_boosted_monster |  |
| PASSED | Machine_Duplication_special_summons_up_to_two_matching_low_attack_machines |  |
| PASSED | Magic_Jammer_discards_a_card_to_negate_a_Spell_activation |  |
| PASSED | Majestic_Dragon_can_only_be_used_for_a_Majestic_Synchro_Summon |  |
| PASSED | Majestic_Star_Dragon_can_activate_a_negated_opponents_monster_effect_as_its_own |  |
| PASSED | Majestic_Star_Dragon_negates_an_opposing_monster_and_returns_to_the_Extra_Deck |  |
| PASSED | Majestic_Star_Dragon_returns_to_the_Extra_Deck_and_Special_Summons_Stardust_in_the_End_Phase |  |
| PASSED | Majestic_Star_Dragon_tributes_itself_to_negate_an_activation_and_destroy_the_opponents_field |  |
| PASSED | Mirror_Force_destroys_all_attacking_monsters_when_the_opponent_declares_an_attack |  |
| PASSED | Mirror_Force_destroys_all_opposing_attack_position_monsters |  |
| PASSED | Mock_duel_AI_Sangan_selects_a_valid_monster_automatically |  |
| PASSED | Mock_duel_Sangan_can_only_search_once_per_turn |  |
| PASSED | Mock_duel_Sangan_does_not_trigger_when_not_sent_from_the_field |  |
| PASSED | Mock_duel_Sangan_searches_a_legal_monster_and_applies_its_activation_restriction |  |
| PASSED | Mock_duel_does_not_consume_mystical_space_typhoon_for_an_invalid_target |  |
| PASSED | Mock_duel_normal_summon_uses_the_dropped_monster_zone |  |
| PASSED | Mock_duel_normal_summons_a_CSV_monster |  |
| PASSED | Mock_duel_resolves_mystical_space_typhoon_against_a_set_card |  |
| PASSED | Mock_duel_ritual_summons_with_a_ritual_spell_and_enough_material_levels |  |
| PASSED | Mock_duel_sets_a_monster_face_down_in_defense_position |  |
| PASSED | Mock_duel_simulator_provides_Bora_with_another_Blackwing |  |
| PASSED | Mock_duel_simulator_starts_with_selected_card_and_configured_fixtures |  |
| PASSED | Mock_duel_synchro_summons_using_imported_tuner_metadata |  |
| PASSED | Mock_duel_tribute_summon_can_replace_a_tribute_in_the_dropped_zone |  |
| PASSED | Mock_duel_tribute_summons_a_level_five_monster |  |
| PASSED | Monster_Reborn_special_summons_a_target_from_either_Graveyard |  |
| PASSED | Morphtronic_Boarden_allows_Morphtronic_direct_attacks_from_defense_position |  |
| PASSED | Morphtronic_Boarden_protects_other_Morphtronics_from_battle_destruction_and_keeps_direct_attacks |  |
| PASSED | Morphtronic_Celfon_in_Defense_Position_looks_at_and_returns_the_top_cards_in_the_same_order |  |
| PASSED | Morphtronic_Celfon_summons_a_low_Level_Morphtronic_from_the_revealed_cards |  |
| PASSED | Morphtronic_Remoten_banishes_a_Graveyard_target_and_searches_its_Level |  |
| PASSED | Morphtronic_Remoten_in_Defense_Position_recovers_a_same_Level_Graveyard_card |  |
| PASSED | Negate_Attack_stops_battle_damage |  |
| PASSED | Nitro_Warrior_gains_ATK_after_a_Spell_and_can_make_its_follow_up_attack |  |
| PASSED | Nobleman_of_Crossout_banishes_target_and_matching_Flip_monsters_in_both_decks |  |
| PASSED | Normal_duel_advances_the_opponents_queued_turn_actions |  |
| PASSED | One_for_One_discards_a_monster_and_summons_a_Level_one_monster_from_the_Deck |  |
| PASSED | Opponent_can_be_selected_to_start_the_duel |  |
| PASSED | Ordinary_tokens_can_be_Tributed_for_a_Tribute_Summon |  |
| PASSED | Phase_change_event_reports_the_started_phase |  |
| PASSED | Pot_of_Avarice_returns_five_Graveyard_monsters_and_draws_two_cards |  |
| PASSED | Pot_of_Greed_draws_two_cards_from_the_Deck |  |
| PASSED | Power_Tool_Dragon_adds_one_of_three_Equip_Spells_and_shuffles_the_others_back |  |
| PASSED | Power_Tool_Dragon_reveals_three_controller_selected_Equip_Spells_and_sends_one_attached_Equip_to_prevent_destruction |  |
| PASSED | Quickdraw_Synchron_cannot_be_Special_Summoned_without_a_discard_or_open_zone |  |
| PASSED | Quickdraw_Synchron_discards_a_monster_to_special_summon_itself_from_the_hand |  |
| PASSED | Quickdraw_Synchron_does_not_pay_its_discard_cost_when_Special_Summoning_is_locked |  |
| PASSED | Quickdraw_Synchron_requires_a_monster_to_discard |  |
| PASSED | Quillbolt_Hedgehog_revives_while_a_Tuner_is_controlled_and_is_banished_when_it_leaves |  |
| PASSED | Raigeki_Break_discards_a_card_and_destroys_a_selected_field_card |  |
| PASSED | Raigeki_destroys_all_opponent_monsters_only |  |
| PASSED | Red_Dragon_Archfiend_destroys_all_Defense_Position_monsters_it_attacks |  |
| PASSED | Reinforcement_of_the_Army_searches_only_a_Warrior_of_level_four_or_less |  |
| PASSED | Replay_state_can_be_captured_while_ai_actions_are_pending |  |
| PASSED | Road_Synchron_gains_one_Level_after_attacking_until_End_Phase_and_keeps_its_material_reduction |  |
| PASSED | Road_Synchron_reduces_its_Synchro_Material_Level_by_two_for_other_Synchros |  |
| PASSED | Road_Synchron_reduces_its_level_for_other_Synchro_Summons |  |
| PASSED | Sakuretsu_Armor_destroys_the_attacking_monster |  |
| PASSED | Scapegoat_allows_a_Normal_Set_but_prevents_other_summons_that_turn |  |
| PASSED | Scapegoat_special_summons_up_to_four_tokens_and_locks_summons |  |
| PASSED | Scapegoat_tokens_cannot_be_Tributed_for_a_Tribute_Summon |  |
| PASSED | Scrap_Iron_Scarecrow_negates_an_attack_and_sets_itself_again |  |
| PASSED | Shooting_Star_Dragon_banishes_to_negate_an_attack_and_returns_in_the_next_End_Phase |  |
| PASSED | Shooting_Star_Dragon_excavates_Tuners_to_gain_additional_attacks |  |
| PASSED | Shooting_Star_Dragon_negates_and_destroys_an_opponents_destruction_effect |  |
| PASSED | Silverwind_lets_the_controller_choose_up_to_two_valid_targets_instead_of_choosing_for_them |  |
| PASSED | Silverwind_protects_only_the_first_Blackwing_that_would_be_destroyed_by_battle_each_opponent_turn |  |
| PASSED | Smashing_Ground_destroys_the_opponent_face_up_monster_with_highest_Defense |  |
| PASSED | Solemn_Judgment_pays_half_Life_Points_and_negates_a_Spell_activation |  |
| PASSED | Stardust_Dragon_negates_destruction_and_returns_in_the_End_Phase |  |
| PASSED | Stardust_Xiaolong_revives_when_Stardust_Dragon_is_Synchro_Summoned |  |
| PASSED | Starlight_Road_does_not_consume_its_Set_Trap_for_a_single_card_destruction |  |
| PASSED | Starlight_Road_negates_mass_destruction_and_summons_Stardust_Dragon |  |
| PASSED | Starting_player_gets_five_initial_cards_and_one_opening_draw |  |
| PASSED | Structure_decks_contain_at_least_40_main_deck_cards |  |
| PASSED | Synchron_Explorer_revives_a_Synchron_with_its_effects_negated |  |
| PASSED | TG_Hyper_Librarian_draws_for_each_face_up_copy_when_either_player_Synchro_Summons |  |
| PASSED | Torrential_Tribute_responds_to_a_normal_summon |  |
| PASSED | Tribute_summon_reports_the_summoned_card_and_materials |  |
| PASSED | Troop_Dragon_summons_a_copy_from_the_Deck_when_destroyed_by_battle |  |
| PASSED | Tuning_searches_a_Synchron_Tuner_then_sends_the_new_top_card_to_the_Graveyard |  |
| PASSED | Tuningware_can_be_treated_as_Level_2_for_a_Synchro_Summon_and_still_draws |  |
| PASSED | Turbo_Synchron_summons_a_low_ATK_monster_after_battle_damage |  |
| PASSED | Witch_of_the_Black_Forest_searches_a_monster_with_1500_or_less_DEF_when_sent_from_the_field |  |

## Mock-duel scenarios

| Result | Scenario | Details |
|---|---|---|
| PASS | AI Sangan search | The AI selected an eligible Deck monster automatically without stalling its turn. |
| PASS | Mystical Space Typhoon target validation | Rejected an off-field target without consuming the Quick-Play Spell. |
| PASS | Normal Summon | Giant Soldier of Stone was summoned from the imported catalog. |
| PASS | Mystical Space Typhoon | Destroyed exactly the selected Set card and sent both cards to their owners' Graveyards. |
| PASS | Sangan once per turn | Blocked a second search during the same turn and reset the once-per-turn limiter on the next turn. |
| PASS | Sangan trigger condition | Sangan did not search when it was not sent from a Monster Zone. |
| PASS | Sangan search | Prompted for a Deck monster at or below 1500 ATK, rejected an illegal target, and blocked that name's activation for the turn. |
| PASS | Bora automatic support | The Unit Tests setup placed another Blackwing on the field, allowing Bora to Special Summon. |
| PASS | Mock duel simulator | Focus card, Main Phase, configurable board fixtures, and manual result marking are available. |
| PASS | Synchro Summon | Junk Synchron + Quillbolt Hedgehog summoned Junk Warrior. |

## Per-card behavior status

| Card ID | Card | Result | Card-specific behavior tests |
|---|---|---|---|
| 25862681 | Ancient Fairy Dragon | PASS | Ancient_Fairy_Dragon_does_not_draw_on_Synchro_Summon_and_can_activate_both_Main_Phase_effects: PASSED |
| 73580471 | Black Rose Dragon | PASS | Black_Rose_Dragon_destroys_all_field_cards_when_Synchro_Summoned: PASSED |
| 91351370 | Black Whirlwind | PASS | Black_Whirlwind_searches_a_lower_ATK_Blackwing_after_a_Normal_Summon: PASSED |
| 22835145 | Blackwing - Blizzard the Far North | PASS | Blackwing_Blizzard_revives_a_low_Level_Blackwing_in_Defense_Position: PASSED |
| 49003716 | Blackwing - Bora the Spear | PASS | Blackwing_Bora_can_special_summon_while_another_Blackwing_is_controlled: PASSED |
| 2009101 | Blackwing - Gale the Whirlwind | PASS | Blackwing_Gale_halves_a_face_up_opposing_monsters_attack: PASSED |
| 85215458 | Blackwing - Kalut the Moon Shadow | PASS | Blackwing_Kalut_boosts_a_Blackwing_that_has_declared_an_attack: PASSED |
| 46710683 | Blackwing - Mistral the Silver Shield | PASS | Blackwing_Mistral_prevents_the_next_battle_damage_after_it_is_destroyed: PASSED |
| 58820853 | Blackwing - Shura the Blue Flame | PASS | Blackwing_Shura_summons_a_low_attack_Blackwing_after_battle: PASSED |
| 33236860 | Blackwing - Silverwind the Ascendant | PASS | Blackwing_Silverwind_destroys_up_to_two_lower_DEF_face_up_monsters: PASSED; Silverwind_lets_the_controller_choose_up_to_two_valid_targets_instead_of_choosing_for_them: PASSED; Silverwind_protects_only_the_first_Blackwing_that_would_be_destroyed_by_battle_each_opponent_turn: PASSED |
| 75498415 | Blackwing - Sirocco the Dawn | PASS | Blackwing_Sirocco_boosts_a_selected_Blackwing_by_other_Blackwings_attack: PASSED |
| 72714392 | Blackwing - Vayu the Emblem of Honor | PASS | Blackwing_Vayu_banishes_itself_and_a_Blackwing_to_summon_a_negated_Synchro: PASSED |
| 14785765 | Blackwing - Zephyros the Elite | PASS | Blackwing_Zephyros_returns_a_face_up_card_and_special_summons_once_per_Duel: PASSED; Life_Stream_Dragon_sets_Life_Points_and_prevents_Zephyros_effect_damage: PASSED; Life_Stream_Dragon_sets_LP_only_when_Synchro_Summoned_and_banishes_an_Equip_to_prevent_destruction: PASSED |
| 76913983 | Blackwing Armed Wing | PASS | Blackwing_Armed_Wing_gains_ATK_and_pierces_Defense_Position: PASSED |
| 69031175 | Blackwing Armor Master | PASS | Blackwing_Armor_Master_survives_battle_and_applies_Wedge_Counters: PASSED |
| 14087893 | Book of Moon | PASS | Book_of_Moon_sets_a_face_up_monster_to_face_down_defense: PASSED |
| 97077563 | Call of the Haunted | PASS | Call_of_the_Haunted_revives_a_monster_in_Attack_Position_and_remains_equipped: PASSED |
| 23693634 | Colossal Fighter | PASS | Colossal_Fighter_gains_ATK_for_Warriors_in_both_Graveyards: PASSED; Colossal_Fighter_revives_a_Warrior_when_destroyed_by_battle: PASSED |
| 70095154 | Cyber Dragon | PASS | Cyber_Dragon_can_special_summon_if_the_opponent_controls_more_monsters: PASSED |
| 53129443 | Dark Hole | PASS | Dark_Hole_destroys_monsters_on_both_sides_of_the_field: PASSED |
| 14943837 | Debris Dragon | PASS | Debris_Dragon_revives_and_negates_a_low_ATK_monster_on_Normal_Summon: PASSED |
| 87621407 | Dekoichi the Battlechanted Locomotive | PASS | Dekoichi_draws_when_flip_summoned: PASSED |
| 59839761 | Delta Crow - Anti Reverse | PASS | Delta_Crow_destroys_all_Set_opposing_Spells_and_Traps_with_a_Blackwing: PASSED |
| 53855409 | Doppelwarrior | PASS | Doppelwarrior_creates_two_tokens_when_used_as_Synchro_material: PASSED; Doppelwarrior_special_summons_from_hand_after_a_monster_is_revived: PASSED |
| 3429238 | Drill Warrior | PASS | Drill_Warrior_halves_its_ATK_and_can_attack_directly: PASSED; Drill_Warrior_returns_on_the_next_Standby_Phase_and_recovers_a_monster: PASSED |
| 81439173 | Foolish Burial | PASS | Foolish_Burial_sends_a_selected_monster_from_the_Deck_to_the_Graveyard: PASSED |
| 50091196 | Formula Synchron | PASS | Formula_Synchron_can_Quick_Synchro_Summon_during_the_opponents_Main_Phase_using_itself_and_still_draw: PASSED; Formula_Synchron_draws_when_Synchro_Summoned: PASSED |
| 7391448 | Goyo Guardian | PASS | Goyo_Guardian_takes_control_of_a_monster_it_destroys_by_battle: PASSED |
| 53567095 | Icarus Attack | PASS | Icarus_Attack_tributes_a_Winged_Beast_and_destroys_two_field_cards: PASSED |
| 77585513 | Jinzo | PASS | Jinzo_does_not_block_Trap_activation_when_its_effect_is_negated: PASSED; Jinzo_prevents_Set_Trap_Cards_from_being_activated: PASSED |
| 11069680 | Junk Converter | PASS | Junk_Converter_revives_a_Tuner_when_used_as_Synchro_material: PASSED |
| 63977008 | Junk Synchron | PASS | Junk_Synchron_revives_a_Level_two_or_lower_monster_on_Normal_Summon: PASSED |
| 60800381 | Junk Warrior | PASS | Junk_Warrior_gains_ATK_from_other_low_Level_monsters_after_Synchro_Summoning: PASSED |
| 57421866 | Level Eater | PASS | Level_Eater_reduces_a_high_Level_monster_and_special_summons_itself: PASSED |
| 25165047 | Life Stream Dragon | PASS | Life_Stream_Dragon_sets_Life_Points_and_prevents_Zephyros_effect_damage: PASSED; Life_Stream_Dragon_sets_LP_only_when_Synchro_Summoned_and_banishes_an_Equip_to_prevent_destruction: PASSED |
| 23171610 | Limiter Removal | PASS | Limiter_Removal_doubles_Machine_ATK_then_destroys_the_boosted_monster: PASSED |
| 63995093 | Machine Duplication | PASS | Machine_Duplication_special_summons_up_to_two_matching_low_attack_machines: PASSED |
| 21159309 | Majestic Dragon | PASS | Majestic_Dragon_can_only_be_used_for_a_Majestic_Synchro_Summon: PASSED |
| 7841112 | Majestic Star Dragon | PASS | Majestic_Star_Dragon_can_activate_a_negated_opponents_monster_effect_as_its_own: PASSED; Majestic_Star_Dragon_negates_an_opposing_monster_and_returns_to_the_Extra_Deck: PASSED; Majestic_Star_Dragon_returns_to_the_Extra_Deck_and_Special_Summons_Stardust_in_the_End_Phase: PASSED; Majestic_Star_Dragon_tributes_itself_to_negate_an_activation_and_destroy_the_opponents_field: PASSED |
| 44095762 | Mirror Force | PASS | Mirror_Force_destroys_all_attacking_monsters_when_the_opponent_declares_an_attack: PASSED; Mirror_Force_destroys_all_opposing_attack_position_monsters: PASSED |
| 83764719 | Monster Reborn | PASS | Monster_Reborn_special_summons_a_target_from_either_Graveyard: PASSED |
| 48381268 | Morphtronic Boarden | PASS | Morphtronic_Boarden_allows_Morphtronic_direct_attacks_from_defense_position: PASSED; Morphtronic_Boarden_protects_other_Morphtronics_from_battle_destruction_and_keeps_direct_attacks: PASSED |
| 93542102 | Morphtronic Celfon | PASS | Morphtronic_Celfon_in_Defense_Position_looks_at_and_returns_the_top_cards_in_the_same_order: PASSED; Morphtronic_Celfon_summons_a_low_Level_Morphtronic_from_the_revealed_cards: PASSED |
| 57108202 | Morphtronic Remoten | PASS | Morphtronic_Remoten_banishes_a_Graveyard_target_and_searches_its_Level: PASSED; Morphtronic_Remoten_in_Defense_Position_recovers_a_same_Level_Graveyard_card: PASSED |
| 5318639 | Mystical Space Typhoon | PASS | Mock_duel_does_not_consume_mystical_space_typhoon_for_an_invalid_target: PASSED; Mock_duel_resolves_mystical_space_typhoon_against_a_set_card: PASSED |
| 18013090 | Nitro Warrior | PASS | Nitro_Warrior_gains_ATK_after_a_Spell_and_can_make_its_follow_up_attack: PASSED |
| 2295440 | One for One | PASS | One_for_One_discards_a_monster_and_summons_a_Level_one_monster_from_the_Deck: PASSED |
| 67169062 | Pot of Avarice | PASS | Pot_of_Avarice_returns_five_Graveyard_monsters_and_draws_two_cards: PASSED |
| 2403771 | Power Tool Dragon | PASS | Power_Tool_Dragon_adds_one_of_three_Equip_Spells_and_shuffles_the_others_back: PASSED; Power_Tool_Dragon_reveals_three_controller_selected_Equip_Spells_and_sends_one_attached_Equip_to_prevent_destruction: PASSED |
| 20932152 | Quickdraw Synchron | PASS | Quickdraw_Synchron_cannot_be_Special_Summoned_without_a_discard_or_open_zone: PASSED; Quickdraw_Synchron_discards_a_monster_to_special_summon_itself_from_the_hand: PASSED; Quickdraw_Synchron_does_not_pay_its_discard_cost_when_Special_Summoning_is_locked: PASSED; Quickdraw_Synchron_requires_a_monster_to_discard: PASSED |
| 23571046 | Quillbolt Hedgehog | PASS | Quillbolt_Hedgehog_revives_while_a_Tuner_is_controlled_and_is_banished_when_it_leaves: PASSED |
| 70902743 | Red Dragon Archfiend | PASS | Red_Dragon_Archfiend_destroys_all_Defense_Position_monsters_it_attacks: PASSED |
| 32807846 | Reinforcement of the Army | PASS | Reinforcement_of_the_Army_searches_only_a_Warrior_of_level_four_or_less: PASSED |
| 71971554 | Road Synchron | PASS | Road_Synchron_gains_one_Level_after_attacking_until_End_Phase_and_keeps_its_material_reduction: PASSED; Road_Synchron_reduces_its_Synchro_Material_Level_by_two_for_other_Synchros: PASSED |
| 56120475 | Sakuretsu Armor | PASS | Sakuretsu_Armor_destroys_the_attacking_monster: PASSED |
| 26202165 | Sangan | PASS | Mock_duel_AI_Sangan_selects_a_valid_monster_automatically: PASSED; Mock_duel_Sangan_can_only_search_once_per_turn: PASSED; Mock_duel_Sangan_does_not_trigger_when_not_sent_from_the_field: PASSED; Mock_duel_Sangan_searches_a_legal_monster_and_applies_its_activation_restriction: PASSED |
| 73915051 | Scapegoat | PASS | Scapegoat_allows_a_Normal_Set_but_prevents_other_summons_that_turn: PASSED; Scapegoat_special_summons_up_to_four_tokens_and_locks_summons: PASSED; Scapegoat_tokens_cannot_be_Tributed_for_a_Tribute_Summon: PASSED |
| 98427577 | Scrap-Iron Scarecrow | PASS | Scrap_Iron_Scarecrow_negates_an_attack_and_sets_itself_again: PASSED |
| 24696097 | Shooting Star Dragon | PASS | Shooting_Star_Dragon_banishes_to_negate_an_attack_and_returns_in_the_next_End_Phase: PASSED; Shooting_Star_Dragon_excavates_Tuners_to_gain_additional_attacks: PASSED; Shooting_Star_Dragon_negates_and_destroys_an_opponents_destruction_effect: PASSED |
| 41420027 | Solemn Judgment | PASS | Solemn_Judgment_pays_half_Life_Points_and_negates_a_Spell_activation: PASSED |
| 44508094 | Stardust Dragon | PASS | Stardust_Dragon_negates_destruction_and_returns_in_the_End_Phase: PASSED |
| 68543408 | Stardust Xiaolong | PASS | Stardust_Xiaolong_revives_when_Stardust_Dragon_is_Synchro_Summoned: PASSED |
| 58120309 | Starlight Road | PASS | Starlight_Road_does_not_consume_its_Set_Trap_for_a_single_card_destruction: PASSED; Starlight_Road_negates_mass_destruction_and_summons_Stardust_Dragon: PASSED |
| 36643046 | Synchron Explorer | PASS | Synchron_Explorer_revives_a_Synchron_with_its_effects_negated: PASSED |
| 53582587 | Torrential Tribute | PASS | Torrential_Tribute_responds_to_a_normal_summon: PASSED |
| 96363153 | Tuning | PASS | Tuning_searches_a_Synchron_Tuner_then_sends_the_new_top_card_to_the_Graveyard: PASSED |
| 92676637 | Tuningware | PASS | Tuningware_can_be_treated_as_Level_2_for_a_Synchro_Summon_and_still_draws: PASSED |
| 67270095 | Turbo Synchron | PASS | Turbo_Synchron_summons_a_low_ATK_monster_after_battle_damage: PASSED |

## Additional imported-effect card status

| Card ID | Card | Result | Card-specific behavior tests |
|---|---|---|---|
| 29401950 | Bottomless Trap Hole | PASS | Bottomless_Trap_Hole_destroys_and_banishes_a_1500_ATK_summon: PASSED |
| 72892473 | Card Destruction | PASS | Card_Destruction_discards_the_remaining_hand_then_draws_the_same_count: PASSED |
| 58518520 | Clear Effector | PASS | Clear_Effector_draws_when_used_as_Synchro_material_and_protects_the_Synchro_from_effect_destruction: PASSED |
| 94192409 | Compulsory Evacuation Device | PASS | Compulsory_Evacuation_Device_returns_the_target_to_its_owners_hand: PASSED |
| 34124316 | Cyber Jar | PASS | Cyber_Jar_destroys_monsters_and_processes_up_to_five_cards_from_each_deck: PASSED; Cyber_Jar_flip_effect_resolves_during_battle_before_damage: PASSED |
| 15341821 | Dandylion | PASS | Dandylion_creates_two_tokens_that_can_be_Tributed_after_their_summon_turn: PASSED |
| 70231910 | Dark Core | PASS | Dark_Core_discards_a_card_to_banish_a_face_up_monster: PASSED |
| 70342110 | Dimensional Prison | PASS | Dimensional_Prison_banishes_the_attacking_monster: PASSED |
| 49010598 | Divine Wrath | PASS | Divine_Wrath_discards_to_negate_and_destroy_a_monster_effect_source: PASSED |
| 60082869 | Dust Tornado | PASS | Dust_Tornado_destroys_only_the_selected_opposing_Spell_or_Trap: PASSED |
| 42703248 | Giant Trunade | PASS | Giant_Trunade_returns_Spell_and_Trap_cards_to_their_owners_hands: PASSED |
| 79571449 | Graceful Charity | PASS | Graceful_Charity_draws_three_then_discards_two_other_cards: PASSED |
| 18144507 | Harpie's Feather Duster | PASS | Ai_uses_Harpies_Feather_Duster_when_destroying_four_cards_is_profitable: PASSED; Harpies_Feather_Duster_destroys_only_the_opponents_Spells_and_Traps: PASSED |
| 19613556 | Heavy Storm | PASS | Heavy_Storm_destroys_Spells_and_Traps_on_both_sides: PASSED |
| 83968380 | Jar of Greed | PASS | Jar_of_Greed_uses_the_shared_draw_action_when_its_Set_Trap_is_activated: PASSED |
| 69162969 | Lightning Vortex | PASS | Lightning_Vortex_discards_one_card_and_destroys_only_face_up_opponent_monsters: PASSED |
| 77414722 | Magic Jammer | PASS | Magic_Jammer_discards_a_card_to_negate_a_Spell_activation: PASSED |
| 14315573 | Negate Attack | PASS | Negate_Attack_stops_battle_damage: PASSED |
| 71044499 | Nobleman of Crossout | PASS | Nobleman_of_Crossout_banishes_target_and_matching_Flip_monsters_in_both_decks: PASSED |
| 55144522 | Pot of Greed | PASS | Pot_of_Greed_draws_two_cards_from_the_Deck: PASSED |
| 12580477 | Raigeki | PASS | Raigeki_destroys_all_opponent_monsters_only: PASSED |
| 4178474 | Raigeki Break | PASS | Raigeki_Break_discards_a_card_and_destroys_a_selected_field_card: PASSED |
| 97169186 | Smashing Ground | PASS | Smashing_Ground_destroys_the_opponent_face_up_monster_with_highest_Defense: PASSED |
| 90953320 | T.G. Hyper Librarian | PASS | TG_Hyper_Librarian_draws_for_each_face_up_copy_when_either_player_Synchro_Summons: PASSED |
| 55013285 | Troop Dragon | PASS | Troop_Dragon_summons_a_copy_from_the_Deck_when_destroyed_by_battle: PASSED |
| 78010363 | Witch of the Black Forest | PASS | Witch_of_the_Black_Forest_searches_a_monster_with_1500_or_less_DEF_when_sent_from_the_field: PASSED |

## Running the suite

In Unity, open **Window > General > Test Runner**, choose **EditMode**, and run **DuelPrototype.EditModeTests**. The report is regenerated at `TestReports/Card-Effect-Mock-Duel-Report.md`.
