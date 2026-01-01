using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for admin Azure subscription management (CRUD operations)
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class AzureSubscriptionsController : ControllerBase
{
    private readonly IAzureSubscriptionService _subscriptionService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AzureSubscriptionsController> _logger;

    public AzureSubscriptionsController(
        IAzureSubscriptionService subscriptionService,
        IJwtTokenService jwtTokenService,
        ILogger<AzureSubscriptionsController> logger)
    {
        _subscriptionService = subscriptionService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Get all Azure subscriptions
    /// </summary>
    /// <returns>List of all subscriptions</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AzureSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<AzureSubscriptionResponse>>> GetAllSubscriptions()
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
            return Ok(subscriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all subscriptions");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Get a specific Azure subscription by ID
    /// </summary>
    /// <param name="id">Subscription ID</param>
    /// <returns>Subscription details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AzureSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AzureSubscriptionResponse>> GetSubscription(int id)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            var subscription = await _subscriptionService.GetSubscriptionByIdAsync(id);

            if (subscription == null)
            {
                return NotFound(new { message = "Subscription not found" });
            }

            return Ok(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscription: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Create a new Azure subscription
    /// </summary>
    /// <param name="request">Subscription creation request</param>
    /// <returns>Created subscription</returns>
    [HttpPost]
    [ProducesResponseType(typeof(AzureSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AzureSubscriptionResponse>> CreateSubscription([FromBody] CreateAzureSubscriptionRequest request)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            if (string.IsNullOrWhiteSpace(request.SubscriptionKey))
            {
                return BadRequest(new { message = "Subscription key is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Region))
            {
                return BadRequest(new { message = "Region is required" });
            }

            var subscription = await _subscriptionService.CreateSubscriptionAsync(request);

            if (subscription == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to create subscription" });
            }

            _logger.LogInformation("Azure subscription created: {Id}", subscription.Id);

            return CreatedAtAction(nameof(GetSubscription), new { id = subscription.Id }, subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Update an existing Azure subscription
    /// </summary>
    /// <param name="id">Subscription ID</param>
    /// <param name="request">Subscription update request</param>
    /// <returns>Updated subscription</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(AzureSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AzureSubscriptionResponse>> UpdateSubscription(int id, [FromBody] UpdateAzureSubscriptionRequest request)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            var subscription = await _subscriptionService.UpdateSubscriptionAsync(id, request);

            if (subscription == null)
            {
                return NotFound(new { message = "Subscription not found" });
            }

            _logger.LogInformation("Azure subscription updated: {Id}", id);

            return Ok(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subscription: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Delete an Azure subscription
    /// </summary>
    /// <param name="id">Subscription ID</param>
    /// <returns>Success status</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteSubscription(int id)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            var result = await _subscriptionService.DeleteSubscriptionAsync(id);

            if (!result)
            {
                return BadRequest(new { message = "Subscription not found or is being used by meetings" });
            }

            _logger.LogInformation("Azure subscription deleted: {Id}", id);

            return Ok(new { message = "Subscription deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting subscription: {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    private bool ValidateAdminToken()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return false;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var (isValid, _, _) = _jwtTokenService.ValidateAdminToken(token);

        return isValid;
    }
}
