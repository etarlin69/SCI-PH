export default function NotImplemented() {
    return (
        <div className="flex h-screen items-center justify-center">
            <div className="text-center">
                <h1 className="text-4xl font-bold text-ink mb-4">En construcción</h1>
                <p className="text-ink-muted mb-8">Esta funcionalidad está siendo desarrollada.</p>
                <a href="/" className="px-6 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700">
                    Volver al Dashboard
                </a>
            </div>
        </div>
    );
}
