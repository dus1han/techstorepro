using TechStorePro.Application.Catalog.Parties;
using TechStorePro.Application.Catalog.Products;
using TechStorePro.Application.Catalog.Reference;
using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>Products, services and spare parts (requirements §16).</summary>
[Route("api/v1/products")]
public class ProductsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> List([FromQuery] GetProductsQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetProductQuery(id)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateProductCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason)
    {
        await Mediator.Send(new DeleteProductCommand(id, reason));
        return NoContent();
    }

    /// <summary>Requirements §31: price history with effective dates.</summary>
    [HttpGet("{id:guid}/price-history")]
    public async Task<ActionResult<IReadOnlyCollection<PriceHistoryDto>>> PriceHistory(Guid id) =>
        Ok(await Mediator.Send(new GetPriceHistoryQuery(id)));

    /// <summary>
    /// What this product costs a given customer right now, and why. The POS in P5 calls this on
    /// every line; exposing it here lets the counter answer "why is it this price?" today.
    /// </summary>
    [HttpGet("{id:guid}/price")]
    public async Task<ActionResult<ResolvedPrice>> Price(
        Guid id,
        [FromQuery] Guid? customerId,
        [FromServices] IPriceResolver resolver,
        CancellationToken cancellationToken) =>
        Ok(await resolver.ResolveAsync(id, customerId, asOf: null, cancellationToken));
}

/// <summary>Customers (requirements §14).</summary>
[Route("api/v1/customers")]
public class CustomersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List([FromQuery] GetCustomersQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateCustomerCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(List), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCustomerCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason)
    {
        await Mediator.Send(new DeleteCustomerCommand(id, reason));
        return NoContent();
    }
}

/// <summary>Suppliers, including overseas and repair vendors (requirements §15).</summary>
[Route("api/v1/suppliers")]
public class SuppliersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierDto>>> List([FromQuery] GetSuppliersQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateSupplierCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(List), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateSupplierCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason)
    {
        await Mediator.Send(new DeleteSupplierCommand(id, reason));
        return NoContent();
    }
}

/// <summary>Categories and brands.</summary>
[Route("api/v1/categories")]
public class CategoriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CategoryDto>>> List() =>
        Ok(await Mediator.Send(new GetCategoriesQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateCategoryCommand command) =>
        Ok(await Mediator.Send(command));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCategoryCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason)
    {
        await Mediator.Send(new DeleteCategoryCommand(id, reason));
        return NoContent();
    }
}

[Route("api/v1/brands")]
public class BrandsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<BrandDto>>> List() =>
        Ok(await Mediator.Send(new GetBrandsQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateBrandCommand command) =>
        Ok(await Mediator.Send(command));
}

/// <summary>Tax rates — effective-dated, per requirements §11.</summary>
[Route("api/v1/tax-rates")]
public class TaxRatesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TaxRateDto>>> List() =>
        Ok(await Mediator.Send(new GetTaxRatesQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateTaxRateCommand command) =>
        Ok(await Mediator.Send(command));
}

/// <summary>Price tiers (requirements §31) and discounts (§32).</summary>
[Route("api/v1/price-tiers")]
public class PriceTiersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PriceTierDto>>> List() =>
        Ok(await Mediator.Send(new GetPriceTiersQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreatePriceTierCommand command) =>
        Ok(await Mediator.Send(command));
}

/// <summary>
/// Price lists — what a tier actually charges. Without one, a tier changes nothing and every
/// customer falls back to the product's own selling price.
/// </summary>
[Route("api/v1/price-lists")]
public class PriceListsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PriceListDto>>> List() =>
        Ok(await Mediator.Send(new GetPriceListsQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreatePriceListCommand command) =>
        Ok(await Mediator.Send(command));

    [HttpGet("{id:guid}/items")]
    public async Task<ActionResult<IReadOnlyCollection<PriceListItemDto>>> Items(Guid id) =>
        Ok(await Mediator.Send(new GetPriceListItemsQuery(id)));

    /// <summary>Upserts one product's price on this list.</summary>
    [HttpPut("{id:guid}/items")]
    public async Task<ActionResult<Guid>> SetItem(Guid id, SetPriceListItemCommand command)
    {
        if (id != command.PriceListId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }
}

[Route("api/v1/discounts")]
public class DiscountsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<DiscountDto>>> List() =>
        Ok(await Mediator.Send(new GetDiscountsQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateDiscountCommand command) =>
        Ok(await Mediator.Send(command));
}

/// <summary>Payment methods (requirements §23).</summary>
[Route("api/v1/payment-methods")]
public class PaymentMethodsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodDto>>> List() =>
        Ok(await Mediator.Send(new GetPaymentMethodsQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreatePaymentMethodCommand command) =>
        Ok(await Mediator.Send(command));
}

/// <summary>Currencies and FX rates (requirements §26).</summary>
[Route("api/v1/currencies")]
public class CurrenciesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CurrencyDto>>> List() =>
        Ok(await Mediator.Send(new GetCurrenciesQuery()));

    [HttpGet("fx-rates")]
    public async Task<ActionResult<IReadOnlyCollection<FxRateDto>>> FxRates([FromQuery] string? currencyCode) =>
        Ok(await Mediator.Send(new GetFxRatesQuery(currencyCode)));

    /// <summary>Upserts the rate for one currency on one day. A day has exactly one rate.</summary>
    [HttpPut("fx-rates")]
    public async Task<ActionResult<Guid>> SetFxRate(SetFxRateCommand command) =>
        Ok(await Mediator.Send(command));
}
