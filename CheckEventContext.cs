using Gallop;

namespace SendGameStatusPlugin;

internal sealed class CheckEventContext
{
    public CheckEventData data { get; }

    private CheckEventContext(CheckEventData data)
    {
        this.data = data;
    }

    public static CheckEventContext From(SingleModeMechaCheckEventResponse response) => new(new(response.data));

    public static CheckEventContext From(SingleModeOnsenCheckEventResponse response) => new(new(response.data));
}

internal sealed class CheckEventData
{
    public SingleModeChara chara_info { get; }
    public SingleModeHomeInfo home_info { get; }
    public SingleModeEventInfo[] unchecked_event_array { get; }
    public SingleRaceStartInfo race_start_info { get; }

    public CheckEventData(SingleModeMechaCheckEventResponse.CommonResponse data)
    {
        chara_info = data.chara_info;
        home_info = data.home_info;
        unchecked_event_array = data.unchecked_event_array;
        race_start_info = data.race_start_info;
    }

    public CheckEventData(SingleModeOnsenCheckEventResponse.CommonResponse data)
    {
        chara_info = data.chara_info;
        home_info = data.home_info;
        unchecked_event_array = data.unchecked_event_array;
        race_start_info = data.race_start_info;
    }
}
