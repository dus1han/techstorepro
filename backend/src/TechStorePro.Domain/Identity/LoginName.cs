using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A tenant login, as typed: <c>ahmed@GULF01</c>.
///
/// <b>One field, not two.</b> The user types a single string, because a separate "company code" box on
/// the login form asks people to know something they have no way of discovering — and a dropdown of
/// companies would show every tenant on the platform to anyone who opened the page.
///
/// The company code has to be in there somewhere, though, because <see cref="User.Username"/> is only
/// unique <em>within</em> a company: two shops may each have an "admin", so "admin" alone identifies
/// nobody. Splitting on the <c>@</c> recovers the company, which is why
/// <see cref="User.NormaliseUsername"/> refuses a username that contains one.
///
/// A <see cref="PlatformAdmin"/> does not use this at all — they sign in with a bare username against
/// their own table, and the absence of an <c>@company</c> is exactly what separates the two flows.
/// </summary>
public readonly record struct LoginName(string Username, string CompanyCode)
{
    /// <summary>
    /// Parses <c>name@COMPANY</c>. Both halves come back normalised — username lower-cased, company
    /// code upper-cased — so that case cannot be the invisible reason a login fails.
    /// </summary>
    /// <exception cref="DomainException">
    /// If the string is not of that shape. The message says what the shape is, because a user who has
    /// typed only their username needs to be told what is missing, not merely that they are wrong.
    /// </exception>
    public static LoginName Parse(string login)
    {
        var trimmed = (login ?? string.Empty).Trim();

        var at = trimmed.IndexOf('@');

        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
        {
            throw new DomainException(
                "Sign in with your username and company code, as 'username@COMPANY' — "
                + "for example 'ahmed@GULF01'.");
        }

        return new LoginName(
            User.NormaliseUsername(trimmed[..at]),
            Company.NormaliseCode(trimmed[(at + 1)..]));
    }

    public override string ToString() => $"{Username}@{CompanyCode}";
}
