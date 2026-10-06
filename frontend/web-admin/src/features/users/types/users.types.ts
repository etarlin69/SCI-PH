// DTOs de /api/web/admin/users (SCI-PH.Application/DTOs/Security/UserAdministrationModels.cs).
// Las cuentas no se crean aquí: nacen de una invitación (Patient o ClinicalStaff) y no se eliminan.

export interface UserSummary {
  id: string;
  userName: string;
  email: string | null;
  emailConfirmed: boolean;
  isActive: boolean;
  twoFactorEnabled: boolean;
  lockedOutUntilUtc: string | null;
  roles: string[];
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
}

export interface UserPage {
  items: UserSummary[];
  page: number;
  pageSize: number;
  total: number;
}

export interface UserListQuery {
  search?: string;
  page?: number;
  pageSize?: number;
}
