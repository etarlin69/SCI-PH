import { authService } from '../../auth/services/authService';

/** Perfil del usuario en sesión. */
export const profileService = {
  get: authService.getProfile,
  update: authService.updateProfile,
  changePassword: authService.changePassword,
};
