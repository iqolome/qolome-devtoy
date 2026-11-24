using Windows.Security.Credentials.UI;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>Windows Hello / 用户同意验证门闩（查看敏感信息前）。</summary>
public static class WindowsHelloGate
{
    public static async Task<(bool Ok, string? Error)> VerifyAsync(string message)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
            {
                return (false, DescribeAvailability(availability));
            }

            var result = await UserConsentVerifier.RequestVerificationAsync(message);
            return result switch
            {
                UserConsentVerificationResult.Verified => (true, null),
                UserConsentVerificationResult.Canceled => (false, "已取消验证。"),
                UserConsentVerificationResult.DeviceBusy => (false, "验证设备正忙，请稍后重试。"),
                UserConsentVerificationResult.DeviceNotPresent => (false, "未找到可用于验证的设备。"),
                UserConsentVerificationResult.DisabledByPolicy => (false, "组织策略已禁用 Windows Hello 验证。"),
                UserConsentVerificationResult.NotConfiguredForUser =>
                    (false, "当前账户尚未配置 Windows Hello，请先在系统设置中启用。"),
                _ => (false, "验证未通过。"),
            };
        }
        catch (Exception ex)
        {
            return (false, "无法启动 Windows Hello：" + ex.Message);
        }
    }

    private static string DescribeAvailability(UserConsentVerifierAvailability availability) =>
        availability switch
        {
            UserConsentVerifierAvailability.DeviceNotPresent =>
                "本机没有可用于 Windows Hello 的设备（如指纹、面部或 PIN）。",
            UserConsentVerifierAvailability.NotConfiguredForUser =>
                "当前账户尚未配置 Windows Hello，请先在「设置 → 帐户 → 登录选项」中启用。",
            UserConsentVerifierAvailability.DisabledByPolicy =>
                "组织策略已禁用 Windows Hello。",
            UserConsentVerifierAvailability.DeviceBusy =>
                "验证设备正忙，请稍后重试。",
            _ => "当前无法使用 Windows Hello 验证。",
        };
}
