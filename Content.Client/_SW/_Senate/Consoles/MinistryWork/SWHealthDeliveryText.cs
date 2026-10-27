using System;
using System.Collections.Generic;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

internal static class SWHealthDeliveryText
{
    public static bool Received(SWHealthMaterialView material) =>
        material.DeliveryStatus is "delivered" or "delivered_with_losses" || material.Delivered >= material.Required
        || material.QuantityTolerance > 0 && material.Delivered > material.Required - material.QuantityTolerance;

    public static string Quantity(double quantity) => quantity > 0 && quantity < .000001
        ? "< " + .000001.ToString("0.######") : quantity.ToString("0.######");

    public static string Duration(double hours)
    {
        if (!double.IsFinite(hours)) return "срок неизвестен";
        var minutes = Math.Max(1, Math.Ceiling(Math.Max(0, hours) * 60 - .000001));
        var whole = Math.Floor(minutes / 60);
        var remainder = minutes % 60;
        return whole == 0 ? $"{minutes:0} мин" : remainder == 0 ? $"{whole:0} ч" : $"{whole:0} ч {remainder:0} мин";
    }
    public static IEnumerable<string> Material(SWHealthMaterialView material)
    {
        if (material.DeliveryStatus == "delivered_with_losses") { yield return "Доставлено с потерями"; yield break; }
        if (Received(material))
        {
            yield return material.Delivered < material.Required && material.QuantityTolerance > 0
                ? "Достаточно для производства" : "Получено полностью";
            yield break;
        }
        if (material.DeliveryHours is { } full && double.IsFinite(full))
        {
            yield return full <= 0 ? "Доставка сейчас" : "До доставки: " + Duration(full);
            yield break;
        }
        var wholeInTransit = material.MinimumTravelHours is { } lower && double.IsFinite(lower) && lower > 0;
        if (wholeInTransit)
        {
            yield return "До доставки: не менее " + Duration(material.MinimumTravelHours!.Value);
            yield break;
        }
        if (material.NextDeliveryQuantity > 0 && material.NextDeliveryHours is { } next && double.IsFinite(next))
            yield return $"Ближайшая часть {Quantity(material.NextDeliveryQuantity)}: " + (next <= 0 ? "прибывает сейчас" : "через " + Duration(next)) + "; полный срок неизвестен";
        else if (material.NextDeliveryQuantity > 0 && material.NextMinimumTravelHours is { } nextLower && double.IsFinite(nextLower))
            yield return $"Ближайшая часть {Quantity(material.NextDeliveryQuantity)}: не менее {Duration(nextLower)}; полный срок неизвестен";
        else if (material.MinimumTravelHours == null)
            yield return material.DeliveryStatus switch
            {
                "awaiting_order" => "Заказ ещё не оформлен", "awaiting_publication" => "Заказ принят",
                "waiting_materials" => "Ожидается отправка", "partial_incoming" => "Отправлена часть; полный срок неизвестен",
                "waiting_transport" => "Ожидание транспорта", "fuel" => "Ожидание топлива", "route_blocked" => "Маршрут перекрыт",
                "transshipment" => "Перевалка", "in_transit" => "Груз в пути; срок неизвестен", _ => "Ожидается отправка",
            };
    }
    public static SWHealthMaterialView? ProducedPayload(SWHealthOrderView order) => order.Completed || order.Progress >= 99.999f ? order.PayloadDelivery : null;
    public static string Flow(SWHealthSupplyFlowView flow, double hour, bool paused)
    {
        var eta = flow.RouteBlocked ? "Маршрут перекрыт"
            : flow.DeliveryAtHours is { } delivery && double.IsFinite(delivery)
                ? delivery <= hour && flow.FinalLeg ? "Доставка сейчас"
                    : "До доставки: " + (flow.FinalLeg ? "" : "не менее ") + Duration(Math.Max(0, delivery - hour))
                : "Срок доставки неизвестен";
        return eta + (paused ? "\nНочная пауза" : "");
    }
}
