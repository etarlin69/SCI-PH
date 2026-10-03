import { StatusPage } from '../components/common/StatusPage';

export default function NotFound() {
    return <StatusPage code="404" title="Página no encontrada" message="La dirección no existe o se ha movido. Revisa la URL o vuelve al inicio." />;
}
