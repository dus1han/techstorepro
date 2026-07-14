using TechStorePro.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("financial_accounts");

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Kind).HasConversion<short>();
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(a => a.BankName).HasMaxLength(200);
        builder.Property(a => a.AccountNumber).HasMaxLength(64);
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.DeletedReason).HasMaxLength(500);

        // There is no balance column, deliberately — see FinancialAccount. The balance is the sum of
        // account_transactions, and this index is what makes summing it cheap.
        builder.HasIndex(a => new { a.CompanyId, a.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(a => new { a.CompanyId, a.Kind, a.IsActive });

        // Nullable: a company-wide bank account belongs to no single branch.
        builder.HasOne(a => a.Branch).WithMany().HasForeignKey(a => a.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AccountTransactionConfiguration : IEntityTypeConfiguration<AccountTransaction>
{
    public void Configure(EntityTypeBuilder<AccountTransaction> builder)
    {
        builder.ToTable("account_transactions");

        builder.Property(t => t.Source).HasConversion<short>();
        builder.Property(t => t.SourceNumber).HasMaxLength(50);
        builder.Property(t => t.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(t => t.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(t => t.Reference).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(500).IsRequired();
        builder.Property(t => t.DeletedReason).HasMaxLength(500);

        // Computed from Amount × ExchangeRate. Storing it would be storing a number that can disagree
        // with the two it comes from.
        builder.Ignore(t => t.AmountBase);

        // The index the whole module rests on: every balance is a SUM over one account, and the statement
        // reads the same rows in date order.
        builder.HasIndex(t => new { t.CompanyId, t.FinancialAccountId, t.OccurredAt });

        // "Which movement did this expense write?" — asked when an expense is cancelled, and by anyone
        // walking a statement row back to the document behind it.
        builder.HasIndex(t => new { t.CompanyId, t.Source, t.SourceId });

        builder.HasOne(t => t.FinancialAccount).WithMany(a => a.Transactions)
            .HasForeignKey(t => t.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Branch).WithMany().HasForeignKey(t => t.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories");

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.HasIndex(c => new { c.CompanyId, c.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses");

        builder.Property(e => e.Number).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(e => e.Status).HasConversion<short>();
        builder.Property(e => e.Reference).HasMaxLength(200);
        builder.Property(e => e.CancelledReason).HasMaxLength(500);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.DeletedReason).HasMaxLength(500);

        builder.Ignore(e => e.AmountBase);

        builder.HasIndex(e => new { e.CompanyId, e.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // The P&L reads expenses by date and groups them by category (§35). Both are on this index.
        builder.HasIndex(e => new { e.CompanyId, e.ExpenseDate, e.ExpenseCategoryId });

        builder.HasOne(e => e.ExpenseCategory).WithMany().HasForeignKey(e => e.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.FinancialAccount).WithMany().HasForeignKey(e => e.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable: a parking fee has no supplier.
        builder.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
