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
                name: "ClinicalAuditEntries",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorType = table.Column<int>(type: "integer", nullable: false),
                    ActorIdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalAuditEntries", x => x.Id);
                    table.CheckConstraint("CK_ClinicalAuditEntries_Action_Enum", "\"Action\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_ClinicalAuditEntries_Actor", "((\"ActorType\" = 1) AND (\"ActorIdentityUserId\" IS NOT NULL) AND (\"ActorReference\" IS NULL)) OR ((\"ActorType\" IN (2, 3)) AND (\"ActorIdentityUserId\" IS NULL) AND (\"ActorStaffId\" IS NULL) AND (\"ActorReference\" IS NOT NULL))");
                    table.CheckConstraint("CK_ClinicalAuditEntries_ActorType_Enum", "\"ActorType\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_ClinicalAuditEntries_Entity", "(length(btrim(\"EntityName\")) > 0) AND (length(btrim(\"EntityId\")) > 0)");
                    table.CheckConstraint("CK_ClinicalAuditEntries_Outcome", "((\"Outcome\" = 1) AND (\"FailureReason\" IS NULL)) OR ((\"Outcome\" = 2) AND (\"FailureReason\" IS NOT NULL))");
                    table.CheckConstraint("CK_ClinicalAuditEntries_Outcome_Enum", "\"Outcome\" IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "ClinicalStaffMembers",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GivenNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FamilyNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProfessionalLicenseNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Specialty = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalStaffMembers", x => x.Id);
                    table.CheckConstraint("CK_ClinicalStaffMembers_Names", "(length(btrim(\"GivenNames\")) > 0) AND (length(btrim(\"FamilyNames\")) > 0)");
                    table.CheckConstraint("CK_ClinicalStaffMembers_Status_Enum", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_ClinicalStaffMembers_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "Clinics",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FacilityCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Municipality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                    table.CheckConstraint("CK_Clinics_Names", "(length(btrim(\"Name\")) > 0) AND (length(btrim(\"Province\")) > 0) AND (length(btrim(\"Municipality\")) > 0)");
                    table.CheckConstraint("CK_Clinics_Status_Enum", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_Clinics_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "Medications",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Presentation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Concentration = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DefaultRoute = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medications", x => x.Id);
                    table.CheckConstraint("CK_Medications_DefaultRoute_Enum", "\"DefaultRoute\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)");
                    table.CheckConstraint("CK_Medications_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Medications_Status_Enum", "\"Status\" IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentityStatus = table.Column<int>(type: "integer", nullable: false),
                    GivenNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FamilyNames = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Sex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                    table.CheckConstraint("CK_Patients_IdentityStatus_Enum", "\"IdentityStatus\" IN (1, 2)");
                    table.CheckConstraint("CK_Patients_Names", "(length(btrim(\"GivenNames\")) > 0) AND (length(btrim(\"FamilyNames\")) > 0)");
                    table.CheckConstraint("CK_Patients_Sex_Enum", "\"Sex\" IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateTable(
                name: "Ambulances",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    BaseClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlateNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CallSign = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ambulances", x => x.Id);
                    table.CheckConstraint("CK_Ambulances_Code", "length(btrim(\"Code\")) > 0");
                    table.CheckConstraint("CK_Ambulances_Status_Enum", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_Ambulances_Clinics_BaseClinicId",
                        column: x => x.BaseClinicId,
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
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicStaffAffiliations", x => x.Id);
                    table.CheckConstraint("CK_ClinicStaffAffiliations_Dates", "\"EndedAtUtc\" >= \"StartedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_ClinicStaffAffiliations_ClinicalStaffMembers_ClinicalStaffId",
                        column: x => x.ClinicalStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                name: "PatientBiometricReferences",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modality = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SubjectReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RegisteredByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientBiometricReferences", x => x.Id);
                    table.UniqueConstraint("AK_PatientBiometricReferences_Id_PatientId", x => new { x.Id, x.PatientId });
                    table.CheckConstraint("CK_PatientBiometricReferences_Modality_Enum", "\"Modality\" IN (1, 2)");
                    table.CheckConstraint("CK_PatientBiometricReferences_References", "(length(btrim(\"Provider\")) > 0) AND (length(btrim(\"SubjectReference\")) > 0)");
                    table.CheckConstraint("CK_PatientBiometricReferences_Revocation", "((\"Status\" = 1) AND (\"RevokedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"RevokedAtUtc\" IS NOT NULL) AND (\"RevokedAtUtc\" >= \"CreatedAtUtc\"))");
                    table.CheckConstraint("CK_PatientBiometricReferences_Status_Enum", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_PatientBiometricReferences_ClinicalStaffMembers_RegisteredB~",
                        column: x => x.RegisteredByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientIdentifiers", x => x.Id);
                    table.UniqueConstraint("AK_PatientIdentifiers_Id_PatientId", x => new { x.Id, x.PatientId });
                    table.CheckConstraint("CK_PatientIdentifiers_Dates", "(\"ValidUntilUtc\" > \"CreatedAtUtc\") AND (\"VerifiedAtUtc\" >= \"CreatedAtUtc\") AND (\"RevokedAtUtc\" >= \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_PatientIdentifiers_Revocation", "((\"Status\" = 1) AND (\"RevokedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"RevokedAtUtc\" IS NOT NULL) AND (NOT \"IsPrimary\"))");
                    table.CheckConstraint("CK_PatientIdentifiers_Source_Enum", "\"Source\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_PatientIdentifiers_Status_Enum", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_PatientIdentifiers_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_PatientIdentifiers_Values", "(length(btrim(\"Value\")) > 0) AND (length(btrim(\"NormalizedValue\")) > 0)");
                    table.CheckConstraint("CK_PatientIdentifiers_Verification", "\"IsVerified\" = (\"VerifiedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PatientIdentifiers_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientMedicalHistories",
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
                    table.PrimaryKey("PK_PatientMedicalHistories", x => x.Id);
                    table.CheckConstraint("CK_PatientMedicalHistories_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_PatientMedicalHistories_ClinicalStaffMembers_RecordedByStaf~",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientMedicalHistories_Patients_PatientId",
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
                    TemporaryPatientCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IdentifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdentifiedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalEncounters", x => x.Id);
                    table.CheckConstraint("CK_ClinicalEncounters_Clinic", "(\"Type\" = 3) OR (\"ClinicId\" IS NOT NULL)");
                    table.CheckConstraint("CK_ClinicalEncounters_End", "((\"Status\" IN (6, 7)) = (\"EndedAtUtc\" IS NOT NULL)) AND (\"EndedAtUtc\" >= \"StartedAtUtc\")");
                    table.CheckConstraint("CK_ClinicalEncounters_Identification", "((\"PatientId\" IS NULL) AND (\"TemporaryPatientCode\" IS NOT NULL) AND (\"IdentifiedAtUtc\" IS NULL) AND (\"IdentifiedByStaffId\" IS NULL) AND (\"Type\" IN (2, 3))) OR ((\"PatientId\" IS NOT NULL) AND (\"TemporaryPatientCode\" IS NULL) AND (\"IdentifiedAtUtc\" IS NULL) AND (\"IdentifiedByStaffId\" IS NULL)) OR ((\"PatientId\" IS NOT NULL) AND (\"TemporaryPatientCode\" IS NOT NULL) AND (\"IdentifiedAtUtc\" IS NOT NULL) AND (\"IdentifiedByStaffId\" IS NOT NULL) AND (\"IdentifiedAtUtc\" >= \"StartedAtUtc\"))");
                    table.CheckConstraint("CK_ClinicalEncounters_Status_Enum", "\"Status\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_ClinicalEncounters_Type_Enum", "\"Type\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_Ambulances_AmbulanceId",
                        column: x => x.AmbulanceId,
                        principalSchema: "clinical",
                        principalTable: "Ambulances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_ClinicalStaffMembers_IdentifiedByStaffId",
                        column: x => x.IdentifiedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChronicConditions", x => x.Id);
                    table.CheckConstraint("CK_ChronicConditions_Code", "NOT (\"Code\" IS NOT NULL) OR (\"CodeSystem\" IS NOT NULL)");
                    table.CheckConstraint("CK_ChronicConditions_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ChronicConditions_Status_Enum", "\"Status\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_ChronicConditions_PatientMedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "PatientMedicalHistories",
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
                    DoseInstructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Route = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    StoppedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeMedications", x => x.Id);
                    table.CheckConstraint("CK_HomeMedications_Dates", "\"StoppedOn\" >= \"StartedOn\"");
                    table.CheckConstraint("CK_HomeMedications_Name", "length(btrim(\"MedicationName\")) > 0");
                    table.CheckConstraint("CK_HomeMedications_Route_Enum", "\"Route\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)");
                    table.CheckConstraint("CK_HomeMedications_Status_Enum", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_HomeMedications_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalSchema: "clinical",
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HomeMedications_PatientMedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "PatientMedicalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientAllergies",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Substance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reaction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OnsetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientAllergies", x => x.Id);
                    table.CheckConstraint("CK_PatientAllergies_Severity_Enum", "\"Severity\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_PatientAllergies_Status_Enum", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_PatientAllergies_Substance", "length(btrim(\"Substance\")) > 0");
                    table.ForeignKey(
                        name: "FK_PatientAllergies_PatientMedicalHistories_MedicalHistoryId",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "PatientMedicalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientSurgicalHistories",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Facility = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientSurgicalHistories", x => x.Id);
                    table.CheckConstraint("CK_PatientSurgicalHistories_Procedure", "length(btrim(\"ProcedureName\")) > 0");
                    table.ForeignKey(
                        name: "FK_PatientSurgicalHistories_PatientMedicalHistories_MedicalHis~",
                        column: x => x.MedicalHistoryId,
                        principalSchema: "clinical",
                        principalTable: "PatientMedicalHistories",
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
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BlobPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UploadedByIdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalDocuments", x => x.Id);
                    table.CheckConstraint("CK_ClinicalDocuments_Checksum", "(\"ChecksumSha256\" IS NULL) OR (\"ChecksumSha256\" ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("CK_ClinicalDocuments_Lifecycle", "(NOT (\"Status\" = 1) OR (\"UploadedAtUtc\" IS NULL)) AND (NOT (\"Status\" = 2) OR ((\"UploadedAtUtc\" IS NOT NULL) AND (\"ChecksumSha256\" IS NOT NULL))) AND ((\"Status\" = 4) = (\"DeletedAtUtc\" IS NOT NULL))");
                    table.CheckConstraint("CK_ClinicalDocuments_Owner", "(\"PatientId\" IS NULL) <> (\"ClinicalEncounterId\" IS NULL)");
                    table.CheckConstraint("CK_ClinicalDocuments_Size", "\"SizeBytes\" > 0");
                    table.CheckConstraint("CK_ClinicalDocuments_Status_Enum", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_ClinicalDocuments_StorageMetadata", "(length(btrim(\"FileName\")) > 0) AND (length(btrim(\"BlobPath\")) > 0) AND (length(btrim(\"ContentType\")) > 0)");
                    table.CheckConstraint("CK_ClinicalDocuments_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.ForeignKey(
                        name: "FK_ClinicalDocuments_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
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
                    AuthorStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalNotes", x => x.Id);
                    table.CheckConstraint("CK_ClinicalNotes_Content", "length(btrim(\"Content\")) > 0");
                    table.CheckConstraint("CK_ClinicalNotes_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "FK_ClinicalNotes_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalNotes_ClinicalStaffMembers_AuthorStaffId",
                        column: x => x.AuthorStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CodeSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PerformedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalProcedures", x => x.Id);
                    table.UniqueConstraint("AK_ClinicalProcedures_Id_ClinicalEncounterId", x => new { x.Id, x.ClinicalEncounterId });
                    table.CheckConstraint("CK_ClinicalProcedures_Code", "NOT (\"Code\" IS NOT NULL) OR (\"CodeSystem\" IS NOT NULL)");
                    table.CheckConstraint("CK_ClinicalProcedures_Lifecycle", "((\"PerformedAtUtc\" IS NULL) = (\"PerformedByStaffId\" IS NULL)) AND (\"CompletedAtUtc\" >= \"PerformedAtUtc\") AND (((\"Status\" = 1) AND (\"PerformedAtUtc\" IS NULL) AND (\"CompletedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"PerformedAtUtc\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NULL)) OR ((\"Status\" = 3) AND (\"PerformedAtUtc\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NOT NULL)) OR ((\"Status\" = 4) AND (\"CompletedAtUtc\" IS NULL)))");
                    table.CheckConstraint("CK_ClinicalProcedures_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ClinicalProcedures_Status_Enum", "\"Status\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_ClinicalProcedures_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalProcedures_ClinicalStaffMembers_PerformedByStaffId",
                        column: x => x.PerformedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                    OriginClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalTransfers", x => x.Id);
                    table.CheckConstraint("CK_ClinicalTransfers_Dates", "(\"SentAtUtc\" >= \"RequestedAtUtc\") AND (\"ReceivedAtUtc\" >= \"SentAtUtc\") AND (\"CompletedAtUtc\" >= \"ReceivedAtUtc\") AND (\"CancelledAtUtc\" >= \"RequestedAtUtc\")");
                    table.CheckConstraint("CK_ClinicalTransfers_DifferentClinics", "(\"OriginClinicId\" IS NULL) OR (\"OriginClinicId\" <> \"DestinationClinicId\")");
                    table.CheckConstraint("CK_ClinicalTransfers_Lifecycle", "((\"Status\" = 1) AND (\"SentAtUtc\" IS NULL) AND (\"ReceivedAtUtc\" IS NULL) AND (\"CompletedAtUtc\" IS NULL) AND (\"CancelledAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"SentAtUtc\" IS NOT NULL) AND (\"ReceivedAtUtc\" IS NULL) AND (\"CompletedAtUtc\" IS NULL) AND (\"CancelledAtUtc\" IS NULL)) OR ((\"Status\" = 3) AND (\"SentAtUtc\" IS NOT NULL) AND (\"ReceivedAtUtc\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NULL) AND (\"CancelledAtUtc\" IS NULL)) OR ((\"Status\" = 4) AND (\"SentAtUtc\" IS NOT NULL) AND (\"ReceivedAtUtc\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NOT NULL) AND (\"CancelledAtUtc\" IS NULL)) OR ((\"Status\" = 5) AND (\"CancelledAtUtc\" IS NOT NULL) AND (\"ReceivedAtUtc\" IS NULL) AND (\"CompletedAtUtc\" IS NULL))");
                    table.CheckConstraint("CK_ClinicalTransfers_Responsibles", "((\"SentAtUtc\" IS NULL) = (\"SentByStaffId\" IS NULL)) AND ((\"ReceivedAtUtc\" IS NULL) = (\"ReceivedByStaffId\" IS NULL))");
                    table.CheckConstraint("CK_ClinicalTransfers_Status_Enum", "\"Status\" IN (1, 2, 3, 4, 5)");
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
                        name: "FK_ClinicalTransfers_ClinicalStaffMembers_ReceivedByStaffId",
                        column: x => x.ReceivedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_ClinicalStaffMembers_SentByStaffId",
                        column: x => x.SentByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_Clinics_DestinationClinicId",
                        column: x => x.DestinationClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalTransfers_Clinics_OriginClinicId",
                        column: x => x.OriginClinicId,
                        principalSchema: "clinical",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Diagnoses",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CodeSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagnoses", x => x.Id);
                    table.CheckConstraint("CK_Diagnoses_Code", "NOT (\"Code\" IS NOT NULL) OR (\"CodeSystem\" IS NOT NULL)");
                    table.CheckConstraint("CK_Diagnoses_Confirmation", "((\"Type\" = 2) = (\"ConfirmedAtUtc\" IS NOT NULL)) AND (\"ConfirmedAtUtc\" >= \"RecordedAtUtc\")");
                    table.CheckConstraint("CK_Diagnoses_Description", "length(btrim(\"Description\")) > 0");
                    table.CheckConstraint("CK_Diagnoses_Type_Enum", "\"Type\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_Diagnoses_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Diagnoses_ClinicalStaffMembers_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                    Function = table.Column<int>(type: "integer", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnassignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterStaffAssignments", x => x.Id);
                    table.CheckConstraint("CK_EncounterStaffAssignments_Dates", "\"UnassignedAtUtc\" >= \"AssignedAtUtc\"");
                    table.CheckConstraint("CK_EncounterStaffAssignments_Function_Enum", "\"Function\" IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "FK_EncounterStaffAssignments_ClinicalEncounters_ClinicalEncoun~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EncounterStaffAssignments_ClinicalStaffMembers_ClinicalStaf~",
                        column: x => x.ClinicalStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationAttempts",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<int>(type: "integer", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientIdentifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    BiometricReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorIdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderRequestReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationAttempts", x => x.Id);
                    table.CheckConstraint("CK_IdentificationAttempts_Actor", "(\"ActorIdentityUserId\" IS NOT NULL) OR (\"PerformedByStaffId\" IS NOT NULL) OR (\"DeviceReference\" IS NOT NULL)");
                    table.CheckConstraint("CK_IdentificationAttempts_Evidence", "(NOT (\"PatientIdentifierId\" IS NOT NULL) OR (\"Method\" IN (1, 2, 3, 4, 5))) AND (NOT (\"BiometricReferenceId\" IS NOT NULL) OR (\"Method\" IN (6, 7)))");
                    table.CheckConstraint("CK_IdentificationAttempts_FailureReason_Enum", "\"FailureReason\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_IdentificationAttempts_Method_Enum", "\"Method\" IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.CheckConstraint("CK_IdentificationAttempts_Outcome", "((\"Outcome\" = 1) AND (\"PatientId\" IS NOT NULL) AND (\"FailureReason\" IS NULL)) OR ((\"Outcome\" = 2) AND (\"PatientId\" IS NULL) AND (\"FailureReason\" IS NOT NULL))");
                    table.CheckConstraint("CK_IdentificationAttempts_Outcome_Enum", "\"Outcome\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_IdentificationAttempts_ClinicalEncounters_ClinicalEncounter~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentificationAttempts_ClinicalStaffMembers_PerformedByStaf~",
                        column: x => x.PerformedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentificationAttempts_PatientBiometricReferences_Biometric~",
                        columns: x => new { x.BiometricReferenceId, x.PatientId },
                        principalSchema: "clinical",
                        principalTable: "PatientBiometricReferences",
                        principalColumns: new[] { "Id", "PatientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentificationAttempts_PatientIdentifiers_PatientIdentifier~",
                        columns: x => new { x.PatientIdentifierId, x.PatientId },
                        principalSchema: "clinical",
                        principalTable: "PatientIdentifiers",
                        principalColumns: new[] { "Id", "PatientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentificationAttempts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescribedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                    table.UniqueConstraint("AK_Prescriptions_Id_ClinicalEncounterId", x => new { x.Id, x.ClinicalEncounterId });
                    table.CheckConstraint("CK_Prescriptions_Lifecycle", "(NOT (\"Status\" = 1) OR ((\"IssuedAtUtc\" IS NULL) AND (\"CancelledAtUtc\" IS NULL))) AND (NOT (\"Status\" = 2) OR ((\"IssuedAtUtc\" IS NOT NULL) AND (\"CancelledAtUtc\" IS NULL))) AND (NOT (\"Status\" = 3) OR (\"CancelledAtUtc\" IS NOT NULL)) AND (\"ValidUntilUtc\" > \"IssuedAtUtc\")");
                    table.CheckConstraint("CK_Prescriptions_Status_Enum", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_Prescriptions_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Prescriptions_ClinicalStaffMembers_PrescribedByStaffId",
                        column: x => x.PrescribedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Triages",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Observations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PerformedByStaffId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Triages", x => x.Id);
                    table.UniqueConstraint("AK_Triages_Id_ClinicalEncounterId", x => new { x.Id, x.ClinicalEncounterId });
                    table.CheckConstraint("CK_Triages_Priority_Enum", "\"Priority\" IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_Triages_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Triages_ClinicalStaffMembers_PerformedByStaffId",
                        column: x => x.PerformedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
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
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CodeSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ResultText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    ReferenceRange = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ResultedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalResults", x => x.Id);
                    table.CheckConstraint("CK_ClinicalResults_Code", "NOT (\"Code\" IS NOT NULL) OR (\"CodeSystem\" IS NOT NULL)");
                    table.CheckConstraint("CK_ClinicalResults_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ClinicalResults_Status_Enum", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_ClinicalResults_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_ClinicalResults_Value", "(NOT (\"Status\" IN (1, 4)) OR (\"ResultedAtUtc\" IS NULL)) AND (NOT (\"Status\" IN (2, 3)) OR ((\"ResultedAtUtc\" IS NOT NULL) AND ((\"ResultText\" IS NOT NULL) OR (\"NumericValue\" IS NOT NULL))))");
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
                        name: "FK_ClinicalResults_ClinicalStaffMembers_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrescriptionDetails",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dose = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    DoseUnit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Route = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionDetails", x => x.Id);
                    table.UniqueConstraint("AK_PrescriptionDetails_Id_PrescriptionId", x => new { x.Id, x.PrescriptionId });
                    table.CheckConstraint("CK_PrescriptionDetails_Route_Enum", "\"Route\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)");
                    table.CheckConstraint("CK_PrescriptionDetails_Values", "(\"Dose\" > 0) AND (\"DurationDays\" > 0) AND (length(btrim(\"DoseUnit\")) > 0) AND (length(btrim(\"Frequency\")) > 0)");
                    table.ForeignKey(
                        name: "FK_PrescriptionDetails_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalSchema: "clinical",
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDetails_Prescriptions_PrescriptionId",
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
                    TriageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    MeasuredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MeasuredByStaffId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VitalSigns", x => x.Id);
                    table.CheckConstraint("CK_VitalSigns_Range", "((\"Type\" = 1) AND (\"Value\" BETWEEN 0 AND 300) AND (\"Unit\" = '/min')) OR ((\"Type\" = 2) AND (\"Value\" BETWEEN 0 AND 100) AND (\"Unit\" = '/min')) OR ((\"Type\" = 3) AND (\"Value\" BETWEEN 0 AND 300) AND (\"Unit\" = 'mm[Hg]')) OR ((\"Type\" = 4) AND (\"Value\" BETWEEN 0 AND 250) AND (\"Unit\" = 'mm[Hg]')) OR ((\"Type\" = 5) AND (\"Value\" BETWEEN 25 AND 45) AND (\"Unit\" = 'Cel')) OR ((\"Type\" = 6) AND (\"Value\" BETWEEN 0 AND 100) AND (\"Unit\" = '%')) OR ((\"Type\" = 7) AND (\"Value\" BETWEEN 0.2 AND 500) AND (\"Unit\" = 'kg')) OR ((\"Type\" = 8) AND (\"Value\" BETWEEN 20 AND 280) AND (\"Unit\" = 'cm')) OR ((\"Type\" = 9) AND (\"Value\" BETWEEN 0 AND 2000) AND (\"Unit\" = 'mg/dL')) OR ((\"Type\" = 10) AND (\"Value\" BETWEEN 0 AND 10) AND (\"Unit\" = '{score}')) OR ((\"Type\" = 11) AND (\"Value\" BETWEEN 3 AND 15) AND (\"Unit\" = '{score}'))");
                    table.CheckConstraint("CK_VitalSigns_Type_Enum", "\"Type\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)");
                    table.ForeignKey(
                        name: "FK_VitalSigns_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VitalSigns_ClinicalStaffMembers_MeasuredByStaffId",
                        column: x => x.MeasuredByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VitalSigns_Triages_TriageId_ClinicalEncounterId",
                        columns: x => new { x.TriageId, x.ClinicalEncounterId },
                        principalSchema: "clinical",
                        principalTable: "Triages",
                        principalColumns: new[] { "Id", "ClinicalEncounterId" },
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
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrescriptionDetailId = table.Column<Guid>(type: "uuid", nullable: true),
                    Dose = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    DoseUnit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Route = table.Column<int>(type: "integer", nullable: false),
                    AdministeredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AdministeredByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationAdministrations", x => x.Id);
                    table.CheckConstraint("CK_MedicationAdministrations_Dose", "(\"Dose\" > 0) AND (length(btrim(\"DoseUnit\")) > 0)");
                    table.CheckConstraint("CK_MedicationAdministrations_EnteredInError", "NOT (\"Status\" = 2) OR (\"Notes\" IS NOT NULL)");
                    table.CheckConstraint("CK_MedicationAdministrations_Prescription", "NOT (\"PrescriptionDetailId\" IS NOT NULL) OR (\"PrescriptionId\" IS NOT NULL)");
                    table.CheckConstraint("CK_MedicationAdministrations_Route_Enum", "\"Route\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)");
                    table.CheckConstraint("CK_MedicationAdministrations_Status_Enum", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_ClinicalEncounters_ClinicalEncoun~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_ClinicalStaffMembers_Administered~",
                        column: x => x.AdministeredByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalSchema: "clinical",
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_PrescriptionDetails_PrescriptionD~",
                        columns: x => new { x.PrescriptionDetailId, x.PrescriptionId },
                        principalSchema: "clinical",
                        principalTable: "PrescriptionDetails",
                        principalColumns: new[] { "Id", "PrescriptionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationAdministrations_Prescriptions_PrescriptionId_Clin~",
                        columns: x => new { x.PrescriptionId, x.ClinicalEncounterId },
                        principalSchema: "clinical",
                        principalTable: "Prescriptions",
                        principalColumns: new[] { "Id", "ClinicalEncounterId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_BaseClinicId",
                schema: "clinical",
                table: "Ambulances",
                column: "BaseClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_Code",
                schema: "clinical",
                table: "Ambulances",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChronicConditions_MedicalHistoryId",
                schema: "clinical",
                table: "ChronicConditions",
                column: "MedicalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_ActorIdentityUserId_OccurredAtUtc",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                columns: new[] { "ActorIdentityUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_EntityName_EntityId_OccurredAtUtc",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                columns: new[] { "EntityName", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEntries_OccurredAtUtc",
                schema: "clinical",
                table: "ClinicalAuditEntries",
                column: "OccurredAtUtc");

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
                name: "IX_ClinicalEncounters_AmbulanceId",
                schema: "clinical",
                table: "ClinicalEncounters",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_ClinicId_Status",
                schema: "clinical",
                table: "ClinicalEncounters",
                columns: new[] { "ClinicId", "Status" });

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
                name: "IX_ClinicalEncounters_Status_StartedAtUtc",
                schema: "clinical",
                table: "ClinicalEncounters",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_TemporaryPatientCode",
                schema: "clinical",
                table: "ClinicalEncounters",
                column: "TemporaryPatientCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_AuthorStaffId",
                schema: "clinical",
                table: "ClinicalNotes",
                column: "AuthorStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_ClinicalEncounterId_OccurredAtUtc",
                schema: "clinical",
                table: "ClinicalNotes",
                columns: new[] { "ClinicalEncounterId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalProcedures_ClinicalEncounterId_PerformedAtUtc",
                schema: "clinical",
                table: "ClinicalProcedures",
                columns: new[] { "ClinicalEncounterId", "PerformedAtUtc" });

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
                name: "IX_ClinicalStaffMembers_IdentityUserId",
                schema: "clinical",
                table: "ClinicalStaffMembers",
                column: "IdentityUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalStaffMembers_ProfessionalLicenseNumber",
                schema: "clinical",
                table: "ClinicalStaffMembers",
                column: "ProfessionalLicenseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_AmbulanceId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_ClinicalEncounterId_RequestedAtUtc",
                schema: "clinical",
                table: "ClinicalTransfers",
                columns: new[] { "ClinicalEncounterId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_DestinationClinicId_Status",
                schema: "clinical",
                table: "ClinicalTransfers",
                columns: new[] { "DestinationClinicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_OriginClinicId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "OriginClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_ReceivedByStaffId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "ReceivedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_SentByStaffId",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "SentByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalTransfers_Status_RequestedAtUtc",
                schema: "clinical",
                table: "ClinicalTransfers",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_FacilityCode",
                schema: "clinical",
                table: "Clinics",
                column: "FacilityCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicStaffAffiliations_ClinicalStaffId",
                schema: "clinical",
                table: "ClinicStaffAffiliations",
                column: "ClinicalStaffId");

            migrationBuilder.CreateIndex(
                name: "UX_ClinicStaffAffiliations_Active",
                schema: "clinical",
                table: "ClinicStaffAffiliations",
                columns: new[] { "ClinicId", "ClinicalStaffId" },
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_ClinicalEncounterId",
                schema: "clinical",
                table: "Diagnoses",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_RecordedByStaffId",
                schema: "clinical",
                table: "Diagnoses",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_EncounterStaffAssignments_ClinicalStaffId",
                schema: "clinical",
                table: "EncounterStaffAssignments",
                column: "ClinicalStaffId");

            migrationBuilder.CreateIndex(
                name: "UX_EncounterStaffAssignments_Active",
                schema: "clinical",
                table: "EncounterStaffAssignments",
                columns: new[] { "ClinicalEncounterId", "ClinicalStaffId" },
                unique: true,
                filter: "\"UnassignedAtUtc\" IS NULL");

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
                name: "IX_IdentificationAttempts_BiometricReferenceId_PatientId",
                schema: "clinical",
                table: "IdentificationAttempts",
                columns: new[] { "BiometricReferenceId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationAttempts_ClinicalEncounterId_OccurredAtUtc",
                schema: "clinical",
                table: "IdentificationAttempts",
                columns: new[] { "ClinicalEncounterId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationAttempts_Method_OccurredAtUtc",
                schema: "clinical",
                table: "IdentificationAttempts",
                columns: new[] { "Method", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationAttempts_PatientId_OccurredAtUtc",
                schema: "clinical",
                table: "IdentificationAttempts",
                columns: new[] { "PatientId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationAttempts_PatientIdentifierId_PatientId",
                schema: "clinical",
                table: "IdentificationAttempts",
                columns: new[] { "PatientIdentifierId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationAttempts_PerformedByStaffId",
                schema: "clinical",
                table: "IdentificationAttempts",
                column: "PerformedByStaffId");

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
                name: "IX_MedicationAdministrations_PrescriptionDetailId_Prescription~",
                schema: "clinical",
                table: "MedicationAdministrations",
                columns: new[] { "PrescriptionDetailId", "PrescriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationAdministrations_PrescriptionId_ClinicalEncounterId",
                schema: "clinical",
                table: "MedicationAdministrations",
                columns: new[] { "PrescriptionId", "ClinicalEncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_Medications_Code",
                schema: "clinical",
                table: "Medications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Medications_Name",
                schema: "clinical",
                table: "Medications",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_MedicalHistoryId",
                schema: "clinical",
                table: "PatientAllergies",
                column: "MedicalHistoryId");

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
                name: "IX_PatientIdentifiers_Lookup",
                schema: "clinical",
                table: "PatientIdentifiers",
                columns: new[] { "Type", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "UX_PatientIdentifiers_ActivePrimary",
                schema: "clinical",
                table: "PatientIdentifiers",
                columns: new[] { "PatientId", "Type" },
                unique: true,
                filter: "(\"IsPrimary\") AND (\"Status\" = 1)");

            migrationBuilder.CreateIndex(
                name: "UX_PatientIdentifiers_ActiveValue",
                schema: "clinical",
                table: "PatientIdentifiers",
                columns: new[] { "Type", "Issuer", "NormalizedValue" },
                unique: true,
                filter: "(\"Type\" IN (1, 2, 3, 4)) AND (\"Status\" = 1)");

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedicalHistories_PatientId_Version",
                schema: "clinical",
                table: "PatientMedicalHistories",
                columns: new[] { "PatientId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedicalHistories_RecordedByStaffId",
                schema: "clinical",
                table: "PatientMedicalHistories",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_FamilyNames_GivenNames",
                schema: "clinical",
                table: "Patients",
                columns: new[] { "FamilyNames", "GivenNames" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_IdentityUserId",
                schema: "clinical",
                table: "Patients",
                column: "IdentityUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientSurgicalHistories_MedicalHistoryId",
                schema: "clinical",
                table: "PatientSurgicalHistories",
                column: "MedicalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDetails_MedicationId",
                schema: "clinical",
                table: "PrescriptionDetails",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDetails_PrescriptionId",
                schema: "clinical",
                table: "PrescriptionDetails",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_ClinicalEncounterId_CreatedAtUtc",
                schema: "clinical",
                table: "Prescriptions",
                columns: new[] { "ClinicalEncounterId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PrescribedByStaffId",
                schema: "clinical",
                table: "Prescriptions",
                column: "PrescribedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_Triages_ClinicalEncounterId_PerformedAtUtc",
                schema: "clinical",
                table: "Triages",
                columns: new[] { "ClinicalEncounterId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Triages_PerformedByStaffId",
                schema: "clinical",
                table: "Triages",
                column: "PerformedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_ClinicalEncounterId_Type_MeasuredAtUtc",
                schema: "clinical",
                table: "VitalSigns",
                columns: new[] { "ClinicalEncounterId", "Type", "MeasuredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_MeasuredByStaffId",
                schema: "clinical",
                table: "VitalSigns",
                column: "MeasuredByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_TriageId_ClinicalEncounterId",
                schema: "clinical",
                table: "VitalSigns",
                columns: new[] { "TriageId", "ClinicalEncounterId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "Diagnoses",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "EncounterStaffAssignments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "HomeMedications",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "IdentificationAttempts",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "MedicationAdministrations",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientAllergies",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientSurgicalHistories",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "VitalSigns",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalProcedures",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientBiometricReferences",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientIdentifiers",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PrescriptionDetails",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "PatientMedicalHistories",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Triages",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Medications",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Prescriptions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalEncounters",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Ambulances",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ClinicalStaffMembers",
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
