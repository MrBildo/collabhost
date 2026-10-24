using Collabhost.Api.Authorization;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

// The configured admin key is compared in fixed time. Timing is not something a unit test can see,
// so these pin the other half: the fixed-time compare still answers equality exactly, including the
// near-misses a broken byte compare would let through (a prefix, one character off, an extension).
public class AuthKeyResolverTests
{
    private const string _configuredKey = "01KW5KB5AMWMRC8KN5JKZ9BYCV";

    [Theory]
    [InlineData(_configuredKey, true)]
    [InlineData("01KW5KB5AMWMRC8KN5JKZ9BYCW", false)]   // last character differs
    [InlineData("11KW5KB5AMWMRC8KN5JKZ9BYCV", false)]   // first character differs
    [InlineData("01KW5KB5AMWMRC8KN5JKZ9BYC", false)]    // a prefix of the key
    [InlineData("01KW5KB5AMWMRC8KN5JKZ9BYCVX", false)]  // the key plus one character
    [InlineData("01kw5kb5amwmrc8kn5jkz9bycv", false)]   // case matters
    [InlineData("", false)]
    public void KeysMatch_AnswersExactEquality(string presentedKey, bool expected) =>
        AuthKeyResolver.KeysMatch(presentedKey, _configuredKey).ShouldBe(expected);
}
