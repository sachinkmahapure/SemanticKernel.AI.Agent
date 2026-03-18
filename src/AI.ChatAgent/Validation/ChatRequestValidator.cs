using AI.ChatAgent.Models;
using FluentValidation;

namespace AI.ChatAgent.Validation;

/// <summary>FluentValidation rules for <see cref="ChatRequest"/>.</summary>
public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message cannot be empty.")
            .MaximumLength(32_000).WithMessage("Message cannot exceed 32,000 characters.")
            .Must(m => !new[] { "ignore previous instructions", "disregard all prior" }
                .Any(b => m.ToLowerInvariant().Contains(b)))
            .WithMessage("Message contains disallowed content.");

        RuleFor(x => x.SessionId)
            .MaximumLength(64).WithMessage("SessionId cannot exceed 64 characters.")
            .Matches("^[a-zA-Z0-9_-]*$")
            .When(x => !string.IsNullOrWhiteSpace(x.SessionId))
            .WithMessage("SessionId must be alphanumeric.");

        RuleFor(x => x.SystemPrompt)
            .MaximumLength(8_000)
            .When(x => x.SystemPrompt is not null)
            .WithMessage("SystemPrompt cannot exceed 8,000 characters.");
    }
}
