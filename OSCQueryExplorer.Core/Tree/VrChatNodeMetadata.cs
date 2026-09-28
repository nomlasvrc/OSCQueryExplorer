using System.Collections.Frozen;
using System.Text.RegularExpressions;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Core.Tree;

public static partial class VrChatNodeMetadata
{
    private static readonly FrozenDictionary<(string Path, int Index), string> ArgumentDescriptions =
        new Dictionary<(string Path, int Index), string>
        {
            [("/chatbox/input", 0)] = "表示するテキスト（最大144文字・9行）",
            [("/chatbox/input", 1)] = "即時送信（OFFでキーボードへ入力）",
            [("/chatbox/input", 2)] = "通知音（省略時はON）",
            [("/chatbox/typing", 0)] = "入力中インジケーター"
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["/chatbox"] = "VRChat ChatboxのOSC入力です。",
        ["/chatbox/input"] = "VRChat Chatboxへテキストを入力／送信します。引数: text (string)、sendImmediately (bool)、notificationSound (bool、省略時true)。最大144文字・9行です。",
        ["/chatbox/typing"] = "VRChat Chatboxの入力中インジケーターを切り替えます。trueでON、falseでOFFです。",
        ["/input"] = "VRChatの操作入力です。Axisは操作後に0.0、Buttonは再操作前に0へ戻してください。",
        ["/input/Vertical"] = "前後移動Axisです。正で前、負で後ろ、操作終了時は0.0を送ります。",
        ["/input/Horizontal"] = "左右移動Axisです。正で右、負で左、操作終了時は0.0を送ります。",
        ["/input/LookHorizontal"] = "左右の視点操作／VR旋回Axisです。操作終了時は0.0を送ります。",
        ["/input/LookVertical"] = "上下の視点操作Axisです。操作終了時は0.0を送ります。",
        ["/input/UseAxisRight"] = "右手アイテム使用用Axisです。操作終了時は0.0を送ります。",
        ["/input/GrabAxisRight"] = "右手アイテム把持用Axisです。操作終了時は0.0を送ります。",
        ["/input/MoveHoldFB"] = "保持オブジェクトを前後移動するAxisです。操作終了時は0.0を送ります。",
        ["/input/SpinHoldCwCcw"] = "保持オブジェクトを時計／反時計回りに回転するAxisです。操作終了時は0.0を送ります。",
        ["/input/SpinHoldUD"] = "保持オブジェクトを上下に回転するAxisです。操作終了時は0.0を送ります。",
        ["/input/SpinHoldLR"] = "保持オブジェクトを左右に回転するAxisです。操作終了時は0.0を送ります。",
        ["/input/MoveForward"] = "前進Buttonです。1で押下、0で解放します。",
        ["/input/MoveBackward"] = "後退Buttonです。1で押下、0で解放します。",
        ["/input/MoveLeft"] = "左移動Buttonです。1で押下、0で解放します。",
        ["/input/MoveRight"] = "右移動Buttonです。1で押下、0で解放します。",
        ["/input/LookLeft"] = "左旋回Buttonです。1で押下、0で解放します。",
        ["/input/LookRight"] = "右旋回Buttonです。1で押下、0で解放します。",
        ["/input/Jump"] = "ジャンプButtonです。1で押下、0で解放します。ワールド設定により無効な場合があります。",
        ["/input/Run"] = "走るButtonです。1で押下、0で解放します。ワールド設定により無効な場合があります。",
        ["/input/ComfortLeft"] = "VRの左スナップターンButtonです。1で押下、0で解放します。",
        ["/input/ComfortRight"] = "VRの右スナップターンButtonです。1で押下、0で解放します。",
        ["/input/DropRight"] = "右手のPickupを離すButtonです。1で押下、0で解放します。",
        ["/input/DropLeft"] = "左手のPickupを離すButtonです。1で押下、0で解放します。",
        ["/input/UseRight"] = "右手で使用するButtonです。1で押下、0で解放します。",
        ["/input/UseLeft"] = "左手で使用するButtonです。1で押下、0で解放します。",
        ["/input/GrabRight"] = "右手で掴むButtonです。1で押下、0で解放します。",
        ["/input/GrabLeft"] = "左手で掴むButtonです。1で押下、0で解放します。",
        ["/input/PanicButton"] = "セーフモードButtonです。1で押下、0で解放します。",
        ["/input/QuickMenuToggleLeft"] = "左手側Quick Menuの開閉Buttonです。1で押下、0で解放します。",
        ["/input/QuickMenuToggleRight"] = "右手側Quick Menuの開閉Buttonです。1で押下、0で解放します。",
        ["/input/Voice"] = "マイク操作Buttonです。Toggle Voice設定により、押下で切替またはPush-to-Talkとして動作します。",
        ["/avatar"] = "VRChatのローカルアバターに関するOSCノードです。",
        ["/avatar/change"] = "ローカルプレイヤーのアバター読み込み時に、アバターIDをstringで通知します。",
        ["/avatar/parameters"] = "VRChatアバターのExpression Parametersです。実際の型・アクセス可否はOSCQuery情報を優先してください。",
        ["/avatar/eyeheight"] = "アバターの眼高（m）です。読取・書込・変更通知に対応します。",
        ["/avatar/eyeheightmin"] = "ワールドによるアバター眼高UIの最小値（m）です。",
        ["/avatar/eyeheightmax"] = "ワールドによるアバター眼高UIの最大値（m）です。",
        ["/avatar/eyeheightscalingallowed"] = "アバターのスケーリングが許可されているかを示すbool値です。"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsVrChatServiceName(string? name) =>
        name is not null && VrChatServiceNameRegex().IsMatch(name);

    public static string? GetArgumentDescription(string fullPath, int index)
    {
        if (ArgumentDescriptions.TryGetValue((fullPath, index), out var description)) return description;
        if (index != 0) return null;
        if (fullPath.StartsWith("/avatar/parameters/", StringComparison.Ordinal)) return "アバターパラメーター値";
        return Descriptions.TryGetValue(fullPath, out description) &&
            (fullPath.StartsWith("/input/", StringComparison.Ordinal) ||
             fullPath.StartsWith("/avatar/eyeheight", StringComparison.Ordinal))
            ? description
            : null;
    }

    public static void ApplyDescriptions(OscNode root)
    {
        foreach (var node in root.SelfAndDescendants())
        {
            if (!string.IsNullOrWhiteSpace(node.Description)) continue;
            if (Descriptions.TryGetValue(node.FullPath, out var description))
                node.Description = description;
            else if (node.FullPath.StartsWith("/avatar/parameters/", StringComparison.Ordinal))
                node.Description = $"VRChatアバターパラメーター「{node.Name}」です。実際の型・アクセス可否はOSCQuery情報を優先してください。";
        }
    }

    [GeneratedRegex("^VRChat-Client-[A-Za-z0-9]{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VrChatServiceNameRegex();
}
