using Gallop;

static class PackageRamenFixture
{
    public static SingleModeRamenLoadResponse CreateRamenLoadResponse()
        => new()
        {
            data = new()
            {
                single_mode_load_common = new()
                {
                    chara_info = CreateChara(),
                    home_info = new() { command_info_array = BaseTrainingCommands() },
                    unchecked_event_array = []
                },
                ramen_data_set = CreateRamenDataSet()
            }
        };

    public static SingleModeChara CreateChara()
        => new()
        {
            single_mode_chara_id = 1,
            speed = 100,
            stamina = 110,
            power = 120,
            guts = 130,
            wiz = 140,
            max_speed = 1500,
            max_stamina = 1500,
            max_power = 1500,
            max_guts = 1500,
            max_wiz = 1500,
            vital = 80,
            max_vital = 100,
            motivation = 5,
            turn = 2,
            skill_point = 100,
            playing_state = 1,
            skill_array = [],
            skill_tips_array = [],
            skill_upgrade_info_array = [],
            support_card_array = [],
            evaluation_info_array = [],
            training_level_info_array =
            [
                new() { command_id = 101, level = 5 },
                new() { command_id = 105, level = 4 },
                new() { command_id = 102, level = 3 },
                new() { command_id = 103, level = 2 },
                new() { command_id = 106, level = 1 }
            ]
        };

    public static SingleModeRamenDataSet CreateRamenDataSet()
        => new()
        {
            command_info_array = ScenarioTrainingCommands(),
            special_feeling_num = 2,
            command_feeling_info_array =
            [
                new() { command_type = 1, command_id = 101, feeling_id = 1 },
                new() { command_type = 1, command_id = 105, feeling_id = 2 },
                new() { command_type = 1, command_id = 102, feeling_id = 3 },
                new() { command_type = 1, command_id = 103, feeling_id = 3 },
                new() { command_type = 1, command_id = 106, feeling_id = 2 },
                new() { command_type = 1, command_id = 601, feeling_id = 0 },
                new() { command_type = 1, command_id = 602, feeling_id = 0 },
                new() { command_type = 1, command_id = 603, feeling_id = 0 },
                new() { command_type = 1, command_id = 604, feeling_id = 0 },
                new() { command_type = 1, command_id = 605, feeling_id = 0 }
            ],
            training_exec_info_array = [new() { base_command_id = 101, exec_count = 3 }],
            feeling_reduce_turn_info_array =
            [
                CommandFeelingTurns(101, (1, 9), (2, 5), (3, 5)),
                CommandFeelingTurns(105, (1, 4), (2, 4), (3, 3)),
                CommandFeelingTurns(102, (1, 4), (2, 3), (3, 4)),
                CommandFeelingTurns(103, (1, 4), (2, 3), (3, 7)),
                CommandFeelingTurns(106, (1, 6), (2, 7), (3, 5))
            ],
            feeling_turn_info_array = FeelingRewardValues((1, 7), (2, 1), (3, 4)),
            feeling_info_array = [],
            active_effect_array = [new() { effect_category = 1, effect_id = 2, effect_value = 3 }],
            uraf_effect_info = new() { uraf_effect_type = 4, uraf_effect_state = 5 }
        };

    public static SingleModeRamenFeelingReduceTurnInfo CommandFeelingTurns(
        int commandId,
        params (int FeelingId, int Turn)[] values)
        => new()
        {
            command_type = 1,
            command_id = commandId,
            feeling_turn_array =
            [
                .. values.Select(x => new SingleModeRamenReduceFeelingTurn
                {
                    feeling_id = x.FeelingId,
                    turn = x.Turn
                })
            ]
        };

    public static SingleModeRamenFeelingTurnInfo[] FeelingRewardValues(params (int FeelingId, int RewardValue)[] values)
        => [.. values.Select(x => new SingleModeRamenFeelingTurnInfo
        {
            feeling_id = x.FeelingId,
            remain_turn = x.RewardValue
        })];

    public static SingleModeCommandInfo[] BaseTrainingCommands()
        => [.. new[] { 101, 105, 102, 103, 106 }.Select(commandId => new SingleModeCommandInfo
        {
            command_type = 1,
            command_id = commandId,
            is_enable = 1,
            training_partner_array = [],
            tips_event_partner_array = [],
            params_inc_dec_info_array = TrainingParams(commandId),
            failure_rate = commandId == 103 ? 20 : 0
        })];

    public static SingleModeRamenCommandInfo[] ScenarioTrainingCommands()
        => [.. new[] { 101, 105, 102, 103, 106 }.Select(commandId => new SingleModeRamenCommandInfo
        {
            command_type = 1,
            command_id = commandId,
            params_inc_dec_info_array = RamenScenarioTrainingParams(commandId)
        })];

    public static SingleModeParamsIncDecInfo[] RamenScenarioTrainingParams(int commandId)
        => commandId switch
        {
            101 => [new() { target_type = 1, value = 7 }, new() { target_type = 3, value = 2 }, new() { target_type = 30, value = 8 }],
            105 => [new() { target_type = 2, value = 3 }, new() { target_type = 4, value = 1 }, new() { target_type = 30, value = 1 }],
            102 => [new() { target_type = 2, value = 1 }, new() { target_type = 3, value = 1 }, new() { target_type = 30, value = 1 }],
            103 => [new() { target_type = 1, value = 1 }, new() { target_type = 3, value = 1 }, new() { target_type = 4, value = 4 }, new() { target_type = 30, value = 5 }],
            106 => [new() { target_type = 1, value = 1 }, new() { target_type = 5, value = 4 }, new() { target_type = 30, value = 6 }],
            _ => throw new InvalidOperationException($"Unknown Ramen scenario command: {commandId}")
        };

    public static SingleModeParamsIncDecInfo[] TrainingParams(int commandId)
        => [new() { target_type = 1 + Array.IndexOf(new[] { 101, 105, 102, 103, 106 }, commandId), value = 10 },
            new() { target_type = 30, value = 5 }];


}
