namespace Ingest.Core.Common;

/// <summary>
/// A stable, machine-readable application diagnostic. <see cref="Message"/> is the existing
/// en-US compatibility fallback; clients should branch on <see cref="Code"/> and use
/// <see cref="Params"/> as named interpolation/context values.
/// </summary>
public sealed record Diagnostic(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Params)
{
    /// <summary>Create a diagnostic without named parameters.</summary>
    public Diagnostic(string code, string message)
        : this(code, message, EmptyParams)
    {
    }

    /// <summary>Shared empty parameter bag.</summary>
    public static IReadOnlyDictionary<string, object?> EmptyParams { get; } =
        new Dictionary<string, object?>();

    /// <summary>Create a diagnostic from named parameter pairs.</summary>
    public static Diagnostic Create(
        string code,
        string message,
        params (string Name, object? Value)[] parameters) =>
        new(
            code,
            message,
            parameters.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal));
}

/// <summary>
/// Stable diagnostic-code catalogue, grouped by backend domain. Values are wire contracts and
/// must not be renamed when fallback wording changes.
/// </summary>
public static class DiagnosticCodes
{
    public static class Common
    {
        public const string NotFound = "common.not_found";
        public const string Conflict = "common.conflict";
        public const string Forbidden = "common.forbidden";
        public const string Unauthorized = "common.unauthorized";
        public const string ServiceUnavailable = "common.service_unavailable";
        public const string Validation = "common.validation";
        public const string Internal = "common.internal";
        public const string FeatureDisabled = "common.feature_disabled";
    }

    public static class Api
    {
        public const string InvalidRequest = "api.invalid_request";
        public const string MissingRequiredParameter = "api.missing_required_parameter";
        public const string SingleParameterRequired = "api.single_parameter_required";
        public const string UnsupportedOmit = "api.unsupported_omit";
        public const string ServiceOutsideScope = "api.service_outside_scope";
    }

    public static class Submissions
    {
        public const string DuplicatePeriod = "submissions.duplicate_period";
        public const string PendingDuplicatePeriod = "submissions.pending_duplicate_period";
        public const string SchemaNotAssigned = "submissions.schema_not_assigned";
        public const string SchemaDisabled = "submissions.schema_disabled";
        public const string ValueNotDefined = "submissions.value_not_defined";
        public const string CalculatedValueSubmitted = "submissions.calculated_value_submitted";
        public const string ValueDisabled = "submissions.value_disabled";
        public const string ValueRequired = "submissions.value_required";
        public const string RequiredValueMissing = "submissions.required_value_missing";
        public const string ValueType = "submissions.value_type";
        public const string ValueMinimum = "submissions.value_minimum";
        public const string ValueMaximum = "submissions.value_maximum";
        public const string ValueBeforeMinimumDate = "submissions.value_before_minimum_date";
        public const string ValueAfterMaximumDate = "submissions.value_after_maximum_date";
        public const string ValueMinimumLength = "submissions.value_minimum_length";
        public const string ValueMaximumLength = "submissions.value_maximum_length";
        public const string ValueRegex = "submissions.value_regex";
        public const string ValueValidationFailed = "submissions.value_validation_failed";
        public const string ValueValidationError = "submissions.value_validation_error";
        public const string SchemaValidationFailed = "submissions.schema_validation_failed";
        public const string SchemaValidationError = "submissions.schema_validation_error";
        public const string ValueNotModifiable = "submissions.value_not_modifiable";
        public const string GatingEvaluationError = "submissions.gating_evaluation_error";
        public const string SampleDiscardedWithMessage = "submissions.sample_discarded_with_message";
        public const string SampleDiscardedByRule = "submissions.sample_discarded_by_rule";
        public const string WarningRuleTriggered = "submissions.warning_rule_triggered";
        public const string WarningRuleMessage = "submissions.warning_rule_message";
        public const string WarningRuleError = "submissions.warning_rule_error";
        public const string LegacyWarning = "submissions.legacy_warning";
        public const string PublishedCannotBecomeDraft = "submissions.published_cannot_become_draft";
        public const string NotAwaitingApproval = "submissions.not_awaiting_approval";
        public const string WrongOwner = "submissions.wrong_owner";
        public const string CreateWindowNotOpen = "submissions.create_window_not_open";
        public const string CreateWindowClosed = "submissions.create_window_closed";
        public const string ModifyWindowClosed = "submissions.modify_window_closed";
        public const string IngestionClosed = "submissions.ingestion_closed";
    }

    public static class Imports
    {
        public const string InvalidFile = "imports.invalid_file";
        public const string EmptyFile = "imports.empty_file";
        public const string InvalidJson = "imports.invalid_json";
        public const string UnsupportedFormat = "imports.unsupported_format";
        public const string SubmissionsNotArray = "imports.submissions_not_array";
        public const string InvalidRoot = "imports.invalid_root";
        public const string SubmissionNotObject = "imports.submission_not_object";
        public const string SamplesMissing = "imports.samples_missing";
        public const string SampleNamesRequired = "imports.sample_names_required";
        public const string SampleInvalidJson = "imports.sample_invalid_json";
        public const string SubmissionNoSamples = "imports.submission_no_samples";
        public const string MissingColumn = "imports.missing_column";
        public const string RowNamesRequired = "imports.row_names_required";
        public const string RowTimestampInvalid = "imports.row_timestamp_invalid";
        public const string NoDataRows = "imports.no_data_rows";
        public const string NoSubmissions = "imports.no_submissions";
        public const string AccountFileMarker = "imports.accounts.invalid_marker";
        public const string AccountFileInvalidJson = "imports.accounts.invalid_json";
        public const string AccountFileVersion = "imports.accounts.unsupported_version";
        public const string AccountFileEmpty = "imports.accounts.no_accounts";
        public const string AccountEntryNameMissing = "imports.accounts.entry_name_missing";
        public const string AccountEntry = "imports.accounts.entry_failed";
    }

    public static class Accounts
    {
        public const string AlreadyExists = "accounts.already_exists";
        public const string InvalidEmail = "accounts.invalid_email";
        public const string UnknownCapabilities = "accounts.unknown_capabilities";
        public const string InvalidAssignedServices = "accounts.invalid_assigned_services";
        public const string SsoLinksUserOnly = "accounts.sso_links_user_only";
        public const string SsoLinkFieldsRequired = "accounts.sso_link_fields_required";
        public const string DuplicateSsoLink = "accounts.duplicate_sso_link";
        public const string SsoLinkInUse = "accounts.sso_link_in_use";
        public const string DeleteInUse = "accounts.delete_in_use";
        public const string ApiKeyDescriptionTooLong = "accounts.api_key_description_too_long";
        public const string ApiKeyExpiryNotFuture = "accounts.api_key_expiry_not_future";
        public const string ApiKeyExpiryTooDistant = "accounts.api_key_expiry_too_distant";
    }

    public static class Schemas
    {
        public const string AlreadyExists = "schemas.already_exists";
        public const string DeleteInUse = "schemas.delete_in_use";
        public const string VersionNegative = "schemas.version_negative";
        public const string VersionDecreased = "schemas.version_decreased";
        public const string ValueNameInvalid = "schemas.value_name_invalid";
        public const string SinceVersionNegative = "schemas.since_version_negative";
        public const string SinceVersionAfterSchema = "schemas.since_version_after_schema";
        public const string TargetBandOutOfOrder = "schemas.target_band_out_of_order";
        public const string GreenMinWithoutAmberMin = "schemas.green_min_without_amber_min";
        public const string GreenMaxWithoutAmberMax = "schemas.green_max_without_amber_max";
        public const string CalculatedExpressionMissing = "schemas.calculated_expression_missing";
        public const string CalculatedValueRequired = "schemas.calculated_value_required";
        public const string CalculatedExpressionSyntax = "schemas.calculated_expression_syntax";
        public const string CalculatedExpressionAnalysis = "schemas.calculated_expression_analysis";
        public const string CalculatedSelfReference = "schemas.calculated_self_reference";
        public const string CalculatedHistoryReference = "schemas.calculated_history_reference";
        public const string CalculatedUnknownIdentifier = "schemas.calculated_unknown_identifier";
        public const string CalculatedDependencyCycle = "schemas.calculated_dependency_cycle";
        public const string LayoutDepthExceeded = "schemas.layout_depth_exceeded";
        public const string LayoutValueNameMissing = "schemas.layout_value_name_missing";
        public const string LayoutUnknownValue = "schemas.layout_unknown_value";
        public const string LayoutDuplicateValue = "schemas.layout_duplicate_value";
        public const string LayoutSectionCaptionMissing = "schemas.layout_section_caption_missing";
        public const string LayoutUnknownKind = "schemas.layout_unknown_kind";
    }

    public static class Reports
    {
        public const string AlreadyExists = "reports.already_exists";
        public const string MissingUpload = "reports.missing_upload";
        public const string ContentEmpty = "reports.content_empty";
        public const string NameRequired = "reports.name_required";
        public const string DateRangeInvalid = "reports.date_range_invalid";
        public const string TypeUnsupported = "reports.type_unsupported";
        public const string SchemaNotTarget = "reports.schema_not_target";
        public const string SchemaRequiredForMultipleTargets = "reports.schema_required_for_multiple_targets";
        public const string SubmissionIdRequired = "reports.submission_id_required";
        public const string SubmissionSchemaMissing = "reports.submission_schema_missing";
        public const string AggregateSchemaRequired = "reports.aggregate_schema_required";
        public const string FrontMatterUnclosed = "reports.front_matter_unclosed";
        public const string FrontMatterLineInvalid = "reports.front_matter_line_invalid";
        public const string FrontMatterTypeInvalid = "reports.front_matter_type_invalid";
        public const string FrontMatterInlineListInvalid = "reports.front_matter_inline_list_invalid";
        public const string TemplateParseFailed = "reports.template_parse_failed";
        public const string TemplateRenderFailed = "reports.template_render_failed";
    }

    public static class Expressions
    {
        public const string Empty = "expressions.empty";
        public const string TooLong = "expressions.too_long";
        public const string ParseFailed = "expressions.parse_failed";
        public const string UnsupportedTarget = "expressions.unsupported_target";
        public const string BatchMissing = "expressions.batch_missing";
        public const string BatchTooLarge = "expressions.batch_too_large";
    }

    public static class Configuration
    {
        public const string BackupEmpty = "configuration.backup_empty";
        public const string BackupInvalidJson = "configuration.backup_invalid_json";
        public const string BackupInvalidMarker = "configuration.backup_invalid_marker";
        public const string BackupUnsupportedVersion = "configuration.backup_unsupported_version";
        public const string BackupMissingCollections = "configuration.backup_missing_collections";
        public const string ConfigBackupEmpty = "configuration.config_backup_empty";
        public const string ConfigBackupInvalidJson = "configuration.config_backup_invalid_json";
        public const string ConfigBackupInvalidMarker = "configuration.config_backup_invalid_marker";
        public const string ConfigBackupUnsupportedVersion = "configuration.config_backup_unsupported_version";
        public const string ConfigBackupMissingCollections = "configuration.config_backup_missing_collections";
    }

    public static class Comments
    {
        public const string ThreadResolved = "comments.thread_resolved";
        public const string EditForbidden = "comments.edit_forbidden";
        public const string ValueNotOnSchema = "comments.value_not_on_schema";
        public const string TargetTypeUnsupported = "comments.target_type_unsupported";
        public const string TextRequired = "comments.text_required";
        public const string TextTooLong = "comments.text_too_long";
    }

    public static class Events
    {
        public const string LabelRequired = "events.label_required";
        public const string TimestampRequired = "events.timestamp_required";
        public const string IntervalDurationRequired = "events.interval_duration_required";
        public const string InvalidServiceIds = "events.invalid_service_ids";
    }

    public static class Approval
    {
        public const string GlobalDefaultNotAllowed = "approval.global_default_not_allowed";
        public const string ApproverRequired = "approval.approver_required";
        public const string RequiredApproverRequired = "approval.required_approver_required";
        public const string DuplicateServiceOwner = "approval.duplicate_service_owner";
        public const string DuplicateApprover = "approval.duplicate_approver";
        public const string ApproverNotFound = "approval.approver_not_found";
        public const string NotDesignatedApprover = "approval.not_designated_approver";
    }

    public static class Integrations
    {
        public const string ConnectionFailed = "integrations.connection_failed";
        public const string ScheduleHourInvalid = "integrations.schedule_hour_invalid";
        public const string ScheduleMinuteInvalid = "integrations.schedule_minute_invalid";
        public const string ScheduleDayInvalid = "integrations.schedule_day_invalid";
        public const string ScheduleAnchorMonthInvalid = "integrations.schedule_anchor_month_invalid";
        public const string TeamsTargetRequired = "integrations.teams_target_required";
    }

    public static class Webhooks
    {
        public const string DeliveryFailed = "webhooks.delivery_failed";
        public const string NameRequired = "webhooks.name_required";
        public const string UrlRequired = "webhooks.url_required";
        public const string UrlInvalid = "webhooks.url_invalid";
    }

    public static class Email
    {
        public const string DeliveryFailed = "email.delivery_failed";
        public const string AddressInvalid = "email.address_invalid";
        public const string SubjectRequired = "email.subject_required";
        public const string EmailSubjectRequired = "email.email_subject_required";
        public const string TextBodyRequired = "email.text_body_required";
        public const string TemplateParseFailed = "email.template_parse_failed";
        public const string SmtpHostRequired = "email.smtp_host_required";
        public const string SmtpPortInvalid = "email.smtp_port_invalid";
        public const string FromAddressRequired = "email.from_address_required";
        public const string AccountContactMissing = "email.account_contact_missing";
    }
}

/// <summary>Common diagnostic factories used across domain services and API adapters.</summary>
public static class Diagnostics
{
    public static Diagnostic Validation(string code, string message, params (string Name, object? Value)[] parameters) =>
        Diagnostic.Create(code, message, parameters);

    public static class Common
    {
        public static Diagnostic NotFound(string resource, object? id = null) =>
            Diagnostic.Create(
                DiagnosticCodes.Common.NotFound,
                $"{resource} not found.",
                ("resource", resource),
                ("id", id));

        public static Diagnostic Conflict(string message, string? domain = null) =>
            Diagnostic.Create(DiagnosticCodes.Common.Conflict, message, ("domain", domain));

        public static Diagnostic Forbidden(string message, string? reason = null) =>
            Diagnostic.Create(DiagnosticCodes.Common.Forbidden, message, ("reason", reason));

        public static Diagnostic ServiceUnavailable(string message, string? reason = null) =>
            Diagnostic.Create(DiagnosticCodes.Common.ServiceUnavailable, message, ("reason", reason));

        public static Diagnostic LegacyValidation(string message, string? domain = null) =>
            Diagnostic.Create(DiagnosticCodes.Common.Validation, message, ("domain", domain));
    }
}
