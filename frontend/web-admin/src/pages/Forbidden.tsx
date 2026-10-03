import { StatusPage } from '../components/common/StatusPage';

// Destino de las respuestas 403. Las guardas de ruta que redirijan aquí llegarán con el AuthProvider.
export default function Forbidden() {
    return (
        <StatusPage
            code="403"
            title="Acceso denegado"
            message="Tu cuenta no tiene permiso para ver esta sección. Si crees que es un error, contacta con un administrador."
        />
    );
}
