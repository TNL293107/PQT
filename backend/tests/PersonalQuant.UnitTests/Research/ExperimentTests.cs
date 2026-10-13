using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.UnitTests.Research;

/// <summary>
/// Verifies what an experiment may be named.
/// </summary>
public sealed class ExperimentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("momentum-vn30")]
    [InlineData("factors/value.v2")]
    public void A_name_of_the_documented_shape_is_accepted(string name) =>
        Assert.Equal(name, Experiment.Define(name, null, Now).Name);

    [Theory]
    [InlineData("")]
    [InlineData("Momentum")]
    [InlineData("-leading")]
    [InlineData("momentum vn30")]
    [InlineData("momentum-vn30\n")]
    public void A_name_outside_the_documented_shape_is_refused(string name)
    {
        // The same name must land in the same experiment; a trailing newline
        // would open a second one and split the trial count.
        Assert.Throws<DomainValidationException>(() => Experiment.Define(name, null, Now));
    }
}
