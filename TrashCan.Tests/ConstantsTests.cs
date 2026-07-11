using AutoTrash.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using Xunit;

namespace AutoTrash.Tests;

/// <summary>
/// 复核安全底线：容器白名单。
/// 所有断言均使用 FFXIVClientStructs 的真实枚举值（与游戏运行时数值一致，
/// 已通过反射核验 InventoryType 与 GameInventoryType 数值完全等价）。
/// </summary>
public class ConstantsTests
{
    // 白名单：仅背包 1~4 必须返回 true（鞍袋已移出白名单）
    [Theory]
    [InlineData(InventoryType.Inventory1)]
    [InlineData(InventoryType.Inventory2)]
    [InlineData(InventoryType.Inventory3)]
    [InlineData(InventoryType.Inventory4)]
    public void IsEligibleContainer_Whitelist_ReturnsTrue(InventoryType container)
    {
        Assert.True(Constants.IsEligibleContainer(container));
    }

    // 受保护容器（公司仓库/军武库/信箱/关键道具/雇员/房屋/货币/水晶/鞍袋外置物等）必须全部返回 false —— 安全底线
    [Theory]
    [InlineData(InventoryType.EquippedItems)]
    [InlineData(InventoryType.Currency)]
    [InlineData(InventoryType.Crystals)]
    [InlineData(InventoryType.MailEdit)]
    [InlineData(InventoryType.Mail)]
    [InlineData(InventoryType.KeyItems)]
    [InlineData(InventoryType.HandIn)]
    [InlineData(InventoryType.BlockedItems)]
    [InlineData(InventoryType.ArmoryOffHand)]
    [InlineData(InventoryType.ArmoryHead)]
    [InlineData(InventoryType.ArmoryBody)]
    [InlineData(InventoryType.ArmoryHands)]
    [InlineData(InventoryType.ArmoryWaist)]
    [InlineData(InventoryType.ArmoryLegs)]
    [InlineData(InventoryType.ArmoryFeets)]
    [InlineData(InventoryType.ArmoryEar)]
    [InlineData(InventoryType.ArmoryNeck)]
    [InlineData(InventoryType.ArmoryWrist)]
    [InlineData(InventoryType.ArmoryRings)]
    [InlineData(InventoryType.ArmorySoulCrystal)]
    [InlineData(InventoryType.ArmoryMainHand)]
    [InlineData(InventoryType.Cosmopouch1)]
    [InlineData(InventoryType.Cosmopouch2)]
    [InlineData(InventoryType.Invalid)]
    [InlineData(InventoryType.RetainerPage1)]
    [InlineData(InventoryType.RetainerPage7)]
    [InlineData(InventoryType.RetainerEquippedItems)]
    [InlineData(InventoryType.RetainerGil)]
    [InlineData(InventoryType.RetainerCrystals)]
    [InlineData(InventoryType.RetainerMarket)]
    [InlineData(InventoryType.FreeCompanyPage1)]
    [InlineData(InventoryType.FreeCompanyPage5)]
    [InlineData(InventoryType.FreeCompanyGil)]
    [InlineData(InventoryType.FreeCompanyCrystals)]
    [InlineData(InventoryType.HousingExteriorAppearance)]
    [InlineData(InventoryType.HousingExteriorPlacedItems)]
    [InlineData(InventoryType.HousingInteriorAppearance)]
    [InlineData(InventoryType.HousingInteriorPlacedItems1)]
    [InlineData(InventoryType.HousingInteriorPlacedItems12)]
    [InlineData(InventoryType.HousingExteriorStoreroom)]
    [InlineData(InventoryType.HousingInteriorStoreroom1)]
    [InlineData(InventoryType.HousingInteriorStoreroom11)]
    [InlineData(InventoryType.HousingExteriorStoreroom2)]
    public void IsEligibleContainer_ProtectedContainers_ReturnsFalse(InventoryType container)
    {
        Assert.False(Constants.IsEligibleContainer(container));
    }
}
