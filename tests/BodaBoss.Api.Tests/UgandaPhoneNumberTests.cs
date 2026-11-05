using BodaBoss.Api.Domain;
using Xunit;

namespace BodaBoss.Api.Tests;

public sealed class UgandaPhoneNumberTests
{
    [Theory]
    [InlineData("0772 123456", "+256772123456")]
    [InlineData("+256 772 123456", "+256772123456")]
    [InlineData("256772123456", "+256772123456")]
    [InlineData("312123456", "+256312123456")]
    public void TryNormalize_accepts_valid_ugandan_mobile_numbers(string input, string expected)
    {
        var result = UgandaPhoneNumber.TryNormalize(input, out var normalized);

        Assert.True(result);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("077212345")]
    [InlineData("+254772123456")]
    [InlineData("0772abc456")]
    [InlineData("0412123456")]
    [InlineData("+2560772123456")]
    public void TryNormalize_rejects_invalid_phone_numbers(string input)
    {
        var result = UgandaPhoneNumber.TryNormalize(input, out var normalized);

        Assert.False(result);
        Assert.Equal(string.Empty, normalized);
    }
}
