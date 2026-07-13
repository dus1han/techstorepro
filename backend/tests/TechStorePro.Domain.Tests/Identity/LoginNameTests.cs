using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Identity;

/// <summary>
/// <c>ahmed@GULF01</c> is one field, and this is what pulls it apart.
///
/// The whole login scheme rests on this being unambiguous. A username is unique only *within* a
/// company — two shops may each have an "admin" — so a bare username identifies nobody, and the
/// company has to come from somewhere. It comes from here.
/// </summary>
public class LoginNameTests
{
    [Fact]
    public void A_login_splits_into_a_username_and_a_company_code()
    {
        var login = LoginName.Parse("ahmed@GULF01");

        login.Username.Should().Be("ahmed");
        login.CompanyCode.Should().Be("GULF01");
    }

    [Fact]
    public void Case_is_not_allowed_to_be_the_invisible_reason_a_login_fails()
    {
        // "Ahmed@gulf01" and "ahmed@GULF01" are the same person. A user who cannot sign in, and whose
        // password is right, and who is told only "incorrect", would never work out why.
        var login = LoginName.Parse("  Ahmed@gulf01  ");

        login.Username.Should().Be("ahmed");
        login.CompanyCode.Should().Be("GULF01");
        login.ToString().Should().Be("ahmed@GULF01");
    }

    [Theory]
    [InlineData("ahmed")]           // no company at all — the most common mistake
    [InlineData("@GULF01")]         // no username
    [InlineData("ahmed@")]          // no company code
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ahmed@gulf@01")]   // two '@' — which half is the company?
    public void A_login_that_is_not_name_at_company_is_refused(string login)
    {
        var act = () => LoginName.Parse(login);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_error_says_what_the_shape_is_rather_than_merely_that_it_is_wrong()
    {
        // A user who typed only their username has to be told what is missing. "Incorrect" would
        // leave them guessing at a format nobody ever showed them.
        var act = () => LoginName.Parse("ahmed");

        act.Should().Throw<DomainException>().WithMessage("*username@COMPANY*");
    }

    [Fact]
    public void A_username_cannot_contain_an_at_sign()
    {
        // This is what makes the split above unambiguous, and it is why the rule lives on the entity
        // rather than in a validator that some future code path could skip.
        var act = () => User.NormaliseUsername("ahmed@gulf");

        act.Should().Throw<DomainException>().WithMessage("*cannot contain '@'*");
    }

    [Fact]
    public void A_blank_username_is_refused()
    {
        var act = () => User.NormaliseUsername("   ");

        act.Should().Throw<DomainException>().WithMessage("*cannot be blank*");
    }

    [Fact]
    public void A_company_code_is_stored_upper_case()
    {
        Company.NormaliseCode(" gulf01 ").Should().Be("GULF01");
    }
}
