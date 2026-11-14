using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using GoNoGo.Presentation;
using UnityEngine;

// Play 模式裡測試收貨輸送帶：下單幾個零件、列出皮帶上零件的位置、拿走最前面的一個。
// unity command script run --file tools/room/DeliveryTest.cs --entry DeliveryTest.Order --args '[5]'
public static class DeliveryTest
{
    public static string Order(int n)
    {
        var m = GameSession.Mission;
        var ids = SessionRules.ShelfParts(m.Data, m.S).Where(p => !m.S.Owned.Contains(p.Id)).Take(n).Select(p => p.Id).ToList();
        foreach (var id in ids) GameSession.Send(CommandType.CartAdd, id);
        GameSession.Send(CommandType.PlaceOrder);
        return "ordered " + string.Join(",", ids) + " owned=" + m.S.Owned.Count + " phase=" + m.S.Phase;
    }

    public static string List()
    {
        var conv = Object.FindAnyObjectByType<DeliveryConveyor>();
        var items = conv.items.GetComponentsInChildren<PartItem>(false)
            .OrderByDescending(i => i.transform.position.x)
            .Select(i => i.partId + "@" + i.transform.position.x.ToString("0.00"));
        return "busy=" + conv.Busy + " " + string.Join(" ", items);
    }

    public static string PickFront()
    {
        var conv = Object.FindAnyObjectByType<DeliveryConveyor>();
        var front = conv.items.GetComponentsInChildren<PartItem>(false).OrderByDescending(i => i.transform.position.x).FirstOrDefault();
        if (front == null) return "none";
        GameSession.Send(CommandType.Pick, front.partId);
        return "picked " + front.partId;
    }

    public static string Drop()
    {
        GameSession.Send(CommandType.Drop);
        return "dropped";
    }
}
