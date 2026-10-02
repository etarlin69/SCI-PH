using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class InitialClinical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "clinical");

            migrationBuilder.CreateTable(
                name: "ClinicalStaff",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GivenNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FamilyNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    ProfessionalLicenseNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Specialty = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalStaff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clinics",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    FacilityCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Municipality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Medications",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GenericName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BrandName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Strength = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DosageForm = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DefaultRoute = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityStatus = table.Column<int>(type: "integer", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GivenNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FamilyNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    SexAtBirth = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ambulances",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CallSign = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ambulances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ambulances_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicStaffAffiliations",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicStaffAffiliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicStaffAffiliations_ClinicalStaff_ClinicalStaffId",
                        column: x => x.ClinicalStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicStaffAffiliations_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicalHistories",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalHistories_ClinicalStaff_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalHistories_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientBiometricReferences",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modality = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SubjectReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RegisteredByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientBiometricReferences", x => x.Id);
                    table.UniqueConstraint("AK_PatientBiometricReferences_Id_PatientId", x => new { x.Id, x.PatientId });
                    table.ForeignKey(
                        name: "FK_PatientBiometricReferences_ClinicalStaff_RegisteredByStaffId",
                        column: x => x.RegisteredByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientBiometricReferences_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientIdentifiers",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientIdentifiers", x => x.Id);
                    table.CheckConstraint("CK_PatientIdentifier_Value", "length(btrim(\"Value\")) > 0 AND length(btrim(\"NormalizedValue\")) > 0");
                    table.ForeignKey(
                        name: "FK_PatientIdentifiers_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalEncounters",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentificationStatus = table.Column<int>(type: "integer", nullable: false),
                    TemporaryPatientCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IdentifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdentifiedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChiefComplaint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalEncounters", x => x.Id);
                    table.CheckConstraint("CK_Encounter_Dates", "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" >= \"StartedAtUtc\"");
                    table.CheckConstraint("CK_Encounter_Identification", "(\"IdentificationStatus\" = 0 AND \"PatientId\" IS NULL\n    AND \"TemporaryPatientCode\" IS NOT NULL AND length(btrim(\"TemporaryPatientCode\")) > 0\n    AND \"IdentifiedAtUtc\" IS NULL AND \"IdentifiedByStaffId\" IS NULL)\nOR (\"IdentificationStatus\" = 1 AND \"PatientId\" IS NOT NULL\n    AND \"IdentifiedAtUtc\" IS NULL AND \"IdentifiedByStaffId\" IS NULL)\nOR (\"IdentificationStatus\" = 2 AND \"PatientId\" IS NOT NULL\n    AND \"IdentifiedAtUtc\" IS NOT NULL AND \"IdentifiedByStaffId\" IS NOT NULL\n    AND \"IdentifiedAtUtc\" >= \"StartedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_Ambulances_AmbulanceId",
                        column: x => x.AmbulanceId,
                        principalSchema: "clinical",
                        principalTable: "Ambulances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_ClinicalStaff_IdentifiedByStaffId",
                        column: x => x.IdentifiedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Allergies",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Substance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reaction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OnsetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allergies_MedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "MedicalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChronicConditions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CodeSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DiagnosedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChronicConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChronicConditions_MedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "MedicalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HomeMedications",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MedicationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DoseInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FrequencyInstructions = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Route = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    StoppedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeMedications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeMedications_MedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "MedicalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HomeMedications_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalSchema: "clinical",
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SurgicalHistories",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Facility = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurgicalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurgicalHistories_MedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "MedicalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BiometricIdentificationAttempts",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchedPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    BiometricReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Modality = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProviderRequestReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiometricIdentificationAttempts", x => x.Id);
                    table.CheckConstraint("CK_BiometricAttempt_Match", "((\"Outcome\" = 0 AND \"MatchedPatientId\" IS NOT NULL) OR (\"Outcome\" <> 0 AND \"MatchedPatientId\" IS NULL)) AND (\"BiometricReferenceId\" IS NULL OR \"MatchedPatientId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BiometricIdentificationAttempts_ClinicalEncounters_Clinical~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BiometricIdentificationAttempts_ClinicalStaff_PerformedBySt~",
                        column: x => x.PerformedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BiometricIdentificationAttempts_PatientBiometricReferences_~",
                        columns: x => new { x.BiometricReferenceId, x.MatchedPatientId },
                        principalSchema: "clinical",
                        principalTable: "PatientBiometricReferences",
                        principalColumns: new[] { "Id", "PatientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BiometricIdentificationAttempts_Patients_MatchedPatientId",
                        column: x => x.MatchedPatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalAuditEntries",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorIdentityUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalAuditEntries_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalAuditEntries_ClinicalStaff_ActorStaffId",
                        column: x => x.ActorStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalAuditEntries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalDocuments",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BlobPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalDocuments", x => x.Id);
                    table.CheckConstraint("CK_Document_Available", "\"Status\" <> 1 OR (\"UploadedAtUtc\" IS NOT NULL AND \"ChecksumSha256\" IS NOT NULL)");
                    table.CheckConstraint("CK_Document_Checksum", "\"ChecksumSha256\" IS NULL OR \"ChecksumSha256\" ~ '^[0-9a-fA-F]{64}$'");
                    table.CheckConstraint("CK_Document_Owner", "\"PatientId\" IS NOT NULL OR \"ClinicalEncounterId\" IS NOT NULL");
                    table.CheckConstraint("CK_Document_Size", "\"SizeBytes\" >= 0");
                    table.CheckConstraint("CK_Document_StorageMetadata", "length(btrim(\"BlobPath\")) > 0 AND length(btrim(\"ContentType\")) > 0 AND length(btrim(\"FileName\")) > 0");
                    table.ForeignKey(
                        name: "FK_ClinicalDocuments_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalDocuments_ClinicalStaff_UploadedByStaffId",
                        column: x => x.UploadedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalDocuments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalNotes",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalNotes_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalNotes_ClinicalStaff_AuthorStaffId",
                        column: x => x.AuthorStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalProcedures",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalProcedures", x => x.Id);
                    table.UniqueConstraint("AK_ClinicalProcedures_Id_ClinicalEncounterId", x => new { x.Id, x.ClinicalEncounterId });
                    table.ForeignKey(
                        name: "FK_ClinicalProcedures_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalProcedures_ClinicalStaff_PerformedByStaffId",
                        column: x => x.PerformedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalTransfers",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_Ambulances_AmbulanceId",
                        column: x => x.AmbulanceId,
                        principalSchema: "clinical",
                        principalTable: "Ambulances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_Clinics_FromClinicId",
                        column: x => x.FromClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_Clinics_ToClinicId",
                        column: x => x.ToClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterDiagnoses",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeSystem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterDiagnoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EncounterDiagnoses_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EncounterDiagnoses_ClinicalStaff_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterStaffAssignments",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnassignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterStaffAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EncounterStaffAssignments_ClinicalEncounters_ClinicalEncoun~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EncounterStaffAssignments_ClinicalStaff_ClinicalStaffId",
                        column: x => x.ClinicalStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicationAdministrations",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministeredByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dose = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    DoseUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Route = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AdministeredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationAdministrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_ClinicalEncounters_ClinicalEncoun~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_ClinicalStaff_AdministeredByStaff~",
                        column: x => x.AdministeredByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalSchema: "clinical",
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescribedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prescriptions_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Prescriptions_ClinicalStaff_PrescribedByStaffId",
                        column: x => x.PrescribedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Prescriptions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TriageAssessments",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriorityLevel = table.Column<int>(type: "integer", nullable: true),
                    PriorityLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageAssessments", x => x.Id);
                    table.UniqueConstraint("AK_TriageAssessments_Id_ClinicalEncounterId", x => new { x.Id, x.ClinicalEncounterId });
                    table.ForeignKey(
                        name: "FK_TriageAssessments_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TriageAssessments_ClinicalStaff_AssessedByStaffId",
                        column: x => x.AssessedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalResults",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalProcedureId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CodeSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ResultText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ReferenceRange = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalResults_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalResults_ClinicalProcedures_ClinicalProcedureId_Clin~",
                        columns: x => new { x.ClinicalProcedureId, x.ClinicalEncounterId },
                        principalSchema: "clinical",
                        principalTable: "ClinicalProcedures",
                        principalColumns: new[] { "Id", "ClinicalEncounterId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalResults_ClinicalStaff_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrescriptionItems",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoseInstructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FrequencyInstructions = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Route = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    QuantityPrescribed = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    QuantityUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorizedRefills = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrescriptionItems_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalSchema: "clinical",
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionItems_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalSchema: "clinical",
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VitalSigns",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriageAssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    MeasuredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VitalSigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VitalSigns_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VitalSigns_ClinicalStaff_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VitalSigns_TriageAssessments_TriageAssessmentId_ClinicalEnc~",
                        columns: x => new { x.TriageAssessmentId, x.ClinicalEncounterId },
                        principalSchema: "clinical",
                        principalTable: "TriageAssessments",
                        principalColumns: new[] { "Id", "ClinicalEncounterId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allergies_MedicalHistoryId",
                schema: "clinical",
                table: "Allergies",
                column: "MedicalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_ClinicId",
                schema: "clinical",
                table: "Ambulances",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_UnitCode",
                schema: "clinical",
                table: "Ambulances",
                column: "UnitCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BiometricIdentificationAttempts_BiometricReferenceId_Matche~",
                schema: "clinical",
                table: "BiometricIdentificationAttempts",
                columns: new[] { "BiometricReferenceId", "MatchedPatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_BiometricIdentificationAttempts_ClinicalEncounterId_Attempt~",
                schema: "clinical",
                table: "BiometricIdentificationAttempts",
                columns: new[] { "ClinicalEncounterId", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BiometricIdentificationAttempts_MatchedPatientId_AttemptedA~",
                schema: "clinical",
                table: "BiometricIdentificationAttempts",
                columns: new[] { "MatchedPatientId", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BiometricIdentificationAttempts_PerformedByStaffId",
                schema: "clinical",
                table: "BiometricIdentificationAttempts",
                column: "PerformedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ChronicConditions_MedicalHistoryId",
                schema: "clinical",
                table: "ChronicConditions",
                column: "MedicalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_ActorStaffId",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                column: "ActorStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_ClinicalEncounterId",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_EntityName_EntityId_OccurredAtUtc",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                columns: new[] { "EntityName", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_PatientId_OccurredAtUtc",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                columns: new[] { "PatientId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_BlobPath",
                schema: "clinical",
                table: "ClinicalDocuments",
                column: "BlobPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_ClinicalEncounterId_CreatedAtUtc",
                schema: "clinical",
                table: "ClinicalDocuments",
                columns: new[] { "ClinicalEncounterId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_PatientId_CreatedAtUtc",
                schema: "clinical",
                table: "ClinicalDocuments",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_UploadedByStaffId",
                schema: "clinical",
                table: "ClinicalDocuments",
                column: "UploadedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_AmbulanceId",
                schema: "clinical",
                table: "ClinicalEncounters",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_ClinicId_StartedAtUtc",
                schema: "clinical",
                table: "ClinicalEncounters",
                columns: new[] { "ClinicId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_IdentifiedByStaffId",
                schema: "clinical",
                table: "ClinicalEncounters",
                column: "IdentifiedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_PatientId_StartedAtUtc",
                schema: "clinical",
                table: "ClinicalEncounters",
                columns: new[] { "PatientId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_TemporaryPatientCode",
                schema: "clinical",
                table: "ClinicalEncounters",
                column: "TemporaryPatientCode",
                unique: true,
                filter: "\"TemporaryPatientCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_AuthorStaffId",
                schema: "clinical",
                table: "ClinicalNotes",
                column: "AuthorStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_ClinicalEncounterId_CreatedAtUtc",
                schema: "clinical",
                table: "ClinicalNotes",
                columns: new[] { "ClinicalEncounterId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalProcedures_ClinicalEncounterId_StartedAtUtc",
                schema: "clinical",
                table: "ClinicalProcedures",
                columns: new[] { "ClinicalEncounterId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalProcedures_PerformedByStaffId",
                schema: "clinical",
                table: "ClinicalProcedures",
                column: "PerformedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalResults_ClinicalEncounterId_ResultedAtUtc",
                schema: "clinical",
                table: "ClinicalResults",
                columns: new[] { "ClinicalEncounterId", "ResultedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalResults_ClinicalProcedureId_ClinicalEncounterId",
                schema: "clinical",
                table: "ClinicalResults",
                columns: new[] { "ClinicalProcedureId", "ClinicalEncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalResults_RecordedByStaffId",
                schema: "clinical",
                table: "ClinicalResults",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalStaff_IdentityUserId",
                schema: "clinical",
                table: "ClinicalStaff",
                column: "IdentityUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_AmbulanceId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_ClinicalEncounterId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_FromClinicId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "FromClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_ToClinicId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "ToClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicStaffAffiliations_ClinicalStaffId",
                schema: "clinical",
                table: "ClinicStaffAffiliations",
                column: "ClinicalStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicStaffAffiliations_ClinicId_ClinicalStaffId",
                schema: "clinical",
                table: "ClinicStaffAffiliations",
                columns: new[] { "ClinicId", "ClinicalStaffId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncounterDiagnoses_ClinicalEncounterId",
                schema: "clinical",
                table: "EncounterDiagnoses",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_EncounterDiagnoses_RecordedByStaffId",
                schema: "clinical",
                table: "EncounterDiagnoses",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_EncounterStaffAssignments_ClinicalEncounterId_ClinicalStaff~",
                schema: "clinical",
                table: "EncounterStaffAssignments",
                columns: new[] { "ClinicalEncounterId", "ClinicalStaffId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncounterStaffAssignments_ClinicalStaffId",
                schema: "clinical",
                table: "EncounterStaffAssignments",
                column: "ClinicalStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeMedications_MedicalHistoryId",
                schema: "clinical",
                table: "HomeMedications",
                column: "MedicalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeMedications_MedicationId",
                schema: "clinical",
                table: "HomeMedications",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalHistories_PatientId_Version",
                schema: "clinical",
                table: "MedicalHistories",
                columns: new[] { "PatientId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalHistories_RecordedByStaffId",
                schema: "clinical",
                table: "MedicalHistories",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationAdministrations_AdministeredByStaffId",
                schema: "clinical",
                table: "MedicationAdministrations",
                column: "AdministeredByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationAdministrations_ClinicalEncounterId_AdministeredA~",
                schema: "clinical",
                table: "MedicationAdministrations",
                columns: new[] { "ClinicalEncounterId", "AdministeredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationAdministrations_MedicationId",
                schema: "clinical",
                table: "MedicationAdministrations",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Medications_Code",
                schema: "clinical",
                table: "Medications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientBiometricReferences_PatientId",
                schema: "clinical",
                table: "PatientBiometricReferences",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientBiometricReferences_Provider_Modality_SubjectReferen~",
                schema: "clinical",
                table: "PatientBiometricReferences",
                columns: new[] { "Provider", "Modality", "SubjectReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientBiometricReferences_RegisteredByStaffId",
                schema: "clinical",
                table: "PatientBiometricReferences",
                column: "RegisteredByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientIdentifiers_PatientId",
                schema: "clinical",
                table: "PatientIdentifiers",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientIdentifiers_Type_Issuer_NormalizedValue",
                schema: "clinical",
                table: "PatientIdentifiers",
                columns: new[] { "Type", "Issuer", "NormalizedValue" },
                unique: true,
                filter: "\"Type\" IN (0, 1, 2, 3) AND \"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientIdentifiers_Type_NormalizedValue",
                schema: "clinical",
                table: "PatientIdentifiers",
                columns: new[] { "Type", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_IdentityUserId",
                schema: "clinical",
                table: "Patients",
                column: "IdentityUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionItems_MedicationId",
                schema: "clinical",
                table: "PrescriptionItems",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionItems_PrescriptionId",
                schema: "clinical",
                table: "PrescriptionItems",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_ClinicalEncounterId",
                schema: "clinical",
                table: "Prescriptions",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PatientId_CreatedAtUtc",
                schema: "clinical",
                table: "Prescriptions",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PrescribedByStaffId",
                schema: "clinical",
                table: "Prescriptions",
                column: "PrescribedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_SurgicalHistories_MedicalHistoryId",
                schema: "clinical",
                table: "SurgicalHistories",
                column: "MedicalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TriageAssessments_AssessedByStaffId",
                schema: "clinical",
                table: "TriageAssessments",
                column: "AssessedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_TriageAssessments_ClinicalEncounterId_AssessedAtUtc",
                schema: "clinical",
                table: "TriageAssessments",
                columns: new[] { "ClinicalEncounterId", "AssessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_ClinicalEncounterId_MeasuredAtUtc",
                schema: "clinical",
                table: "VitalSigns",
                columns: new[] { "ClinicalEncounterId", "MeasuredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_RecordedByStaffId",
                schema: "clinical",
                table: "VitalSigns",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_TriageAssessmentId_ClinicalEncounterId",
                schema: "clinical",
                table: "VitalSigns",
                columns: new[] { "TriageAssessmentId", "ClinicalEncounterId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allergies",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "BiometricIdentificationAttempts",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ChronicConditions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalAuditEntries",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalDocuments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalNotes",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalResults",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalTransfers",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicStaffAffiliations",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "EncounterDiagnoses",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "EncounterStaffAssignments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "HomeMedications",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "MedicationAdministrations",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientIdentifiers",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PrescriptionItems",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SurgicalHistories",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "VitalSigns",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientBiometricReferences",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalProcedures",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Medications",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Prescriptions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "MedicalHistories",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "TriageAssessments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalEncounters",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Ambulances",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalStaff",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Patients",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Clinics",
                schema: "clinical");
        }
    }
}
