using System.Collections.Generic;
using SpaceSim.Vehicles;

namespace SpaceSim.Economy
{
    public enum ShopItemState
    {
        Owned,
        Available,
        /// <summary>Requirement (research milestone) not met yet.</summary>
        Locked,
        /// <summary>Unlocked but the player cannot afford it.</summary>
        Unaffordable
    }

    public enum PurchaseResult
    {
        Success,
        AlreadyOwned,
        NotForSale,
        InsufficientFunds,
        RequirementsNotMet,
        /// <summary>The profile is read-only (saved by a newer game version).</summary>
        ReadOnlyProfile
    }

    /// <summary>A purchasable upgrade and its unlock requirement.</summary>
    public sealed class ShopItem
    {
        public ShopItem(ModuleDefinition module, string requiredMilestone, string effect)
        {
            Module = module;
            RequiredMilestone = requiredMilestone;
            Effect = effect;
        }

        public ModuleDefinition Module { get; }
        /// <summary>Milestone id that must be achieved first, or null.</summary>
        public string RequiredMilestone { get; }
        /// <summary>What buying it changes in the game.</summary>
        public string Effect { get; }
        public int Price => Module.price;
    }

    /// <summary>
    /// The upgrade shop. Every item is a module whose effect is real: it changes mass/Δv when installed (compiler) or
    /// protects against hazards (<see cref="SpaceSim.Planets.HazardModel"/>) or extends sampling (M6).
    /// </summary>
    public static class Shop
    {
        static List<ShopItem> items;

        public static IReadOnlyList<ShopItem> Items => items ?? (items = Build());

        static List<ShopItem> Build() => new List<ShopItem>
        {
            Item(ModuleCatalog.TankK140, null,
                "Больше топлива в ступени → выше идеальный Δv; но и стартовая масса больше (ниже тяга/вес)."),
            Item(ModuleCatalog.EngineSokolM, ResearchMilestones.ReachSpace,
                "Больше тяги и Isp первой ступени: выше тяга/вес и Δv."),
            Item(ModuleCatalog.RadiationShield, ResearchMilestones.ReachSpace,
                "Защита от радиации уровня 1: оборудование не деградирует у Квариса и Моры I; у Теонана — медленнее."),
            Item(ModuleCatalog.RadiationShieldHeavy, ResearchMilestones.EscapeHome,
                "Защита от радиации уровня 2: безопасная работа даже у Теонана, ближайшей к звезде планеты."),
            Item(ModuleCatalog.AcidCoating, ResearchMilestones.FirstOrbit,
                "Кислотостойкость уровня 1: оборудование выдерживает сернокислотную атмосферу Гермеус Моры."),
            Item(ModuleCatalog.CollectorAdvanced, ResearchMilestones.FirstOrbit,
                "Пробоотборник ПБ-2: 30 кг, жидкости, кислоты и редкие материалы."),
        };

        static ShopItem Item(string id, string milestone, string effect) => new ShopItem(ModuleCatalog.Find(id), milestone, effect);

        public static ShopItem Find(string moduleId)
        {
            foreach (var i in Items)
                if (i.Module.id == moduleId) return i;
            return null;
        }

        public static ShopItemState StateOf(PlayerProfile profile, ShopItem item)
        {
            if (profile.ownedModules.Contains(item.Module.id)) return ShopItemState.Owned;
            if (item.RequiredMilestone != null && !profile.HasMilestone(item.RequiredMilestone)) return ShopItemState.Locked;
            return profile.credits >= item.Price ? ShopItemState.Available : ShopItemState.Unaffordable;
        }

        /// <summary>Buys a module. Atomic: on any failure the profile is unchanged.</summary>
        public static PurchaseResult TryPurchase(PlayerProfile profile, string moduleId, out string message)
        {
            ShopItem item = Find(moduleId);
            if (item == null)
            {
                message = "Этот модуль не продаётся.";
                return PurchaseResult.NotForSale;
            }
            switch (StateOf(profile, item))
            {
                case ShopItemState.Owned:
                    message = item.Module.name + " уже куплен.";
                    return PurchaseResult.AlreadyOwned;
                case ShopItemState.Locked:
                    message = item.Module.name + ": сначала — «" + ResearchMilestones.Find(item.RequiredMilestone).Name + "».";
                    return PurchaseResult.RequirementsNotMet;
                case ShopItemState.Unaffordable:
                    message = "Не хватает " + (item.Price - profile.credits) + " кредитов на " + item.Module.name + ".";
                    return PurchaseResult.InsufficientFunds;
            }
            profile.credits -= item.Price;
            profile.ownedModules.Add(item.Module.id);
            message = "Куплено: " + item.Module.name + " за " + item.Price + " кр. Доступно в ангаре.";
            return PurchaseResult.Success;
        }
    }
}
