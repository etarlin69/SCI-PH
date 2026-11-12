#include "provisioning/serial_console.h"

#include <utility>

namespace sciph {

namespace {

constexpr size_t kMaxLine = 4096;

std::string trim(const std::string& text) {
    size_t start = 0;
    size_t end = text.size();
    while (start < end && (text[start] == ' ' || text[start] == '\t' || text[start] == '\r' || text[start] == '\n')) {
        ++start;
    }
    while (end > start && (text[end - 1] == ' ' || text[end - 1] == '\t' || text[end - 1] == '\r' || text[end - 1] == '\n')) {
        --end;
    }
    return text.substr(start, end - start);
}

}  // namespace

SerialConsole::SerialConsole(IConsoleOutput& output, ConfigRepository& configs, const DeviceConfig& current,
                             bool provisioned, bool developmentHttpAllowed, ConsoleHooks hooks)
    : output_(output),
      configs_(configs),
      current_(current),
      provisioned_(provisioned),
      developmentHttpAllowed_(developmentHttpAllowed),
      hooks_(std::move(hooks)) {}

void SerialConsole::handleLine(const std::string& rawLine) {
    if (rawLine.size() > kMaxLine) {
        output_.println("Linea demasiado larga: ignorada");
        return;
    }
    const std::string line = trim(rawLine);
    if (step_ == Step::None) {
        handleCommand(line);
        return;
    }
    if (line == "cancel") {
        cancel();
        return;
    }
    if (step_ == Step::WifiPassword) {
        // Spaces can be part of a Wi-Fi password: only the line ending is removed.
        std::string password = rawLine;
        while (!password.empty() && (password.back() == '\r' || password.back() == '\n')) {
            password.pop_back();
        }
        draft_.wifiPassword = password;
        prompt(Step::ApiUrl);
        return;
    }
    if (step_ == Step::ForgetConfirm) {
        step_ = Step::None;
        if (line != "FORGET") {
            output_.println("Cancelado: no se borro nada");
            return;
        }
        configs_.forget();
        output_.println("Credencial, Wi-Fi, URL y CA borrados. Las plantillas del sensor y el registro en curso se conservan.");
        output_.println("Reiniciando...");
        if (hooks_.restart) {
            hooks_.restart();
        }
        return;
    }
    handleProvisioning(line);
}

void SerialConsole::handleCommand(const std::string& line) {
    if (line.empty()) {
        return;
    }
    if (line == "help") {
        printHelp();
    } else if (line == "status") {
        printStatus();
    } else if (line == "provision") {
        clearDraft();
        output_.println("Configuracion del lector. Escriba 'cancel' en cualquier momento para salir sin guardar.");
        output_.println("Los valores escritos no se muestran de vuelta; el monitor serie puede mostrarlos localmente.");
        prompt(Step::WifiSsid);
    } else if (line == "forget") {
        output_.println("Se borraran la credencial del dispositivo, el Wi-Fi, la URL del Device API y el CA.");
        output_.println("Las plantillas del sensor NO se borran. Escriba FORGET para confirmar o 'cancel'.");
        step_ = Step::ForgetConfirm;
    } else if (line == "reconcile" || line == "reconcile force") {
        if (hooks_.reconcile) {
            hooks_.reconcile(line == "reconcile force");
        }
        output_.println("Reconciliacion de slots solicitada");
    } else if (line == "journal discard") {
        const bool discarded = hooks_.discardUnreadableJournal && hooks_.discardUnreadableJournal();
        output_.println(discarded ? "Registro ilegible descartado" : "No hay un registro ilegible que descartar");
    } else if (line == "restart") {
        output_.println("Reiniciando...");
        if (hooks_.restart) {
            hooks_.restart();
        }
    } else {
        output_.println("Comando desconocido. Escriba 'help'.");
    }
}

void SerialConsole::prompt(Step step) {
    step_ = step;
    switch (step) {
        case Step::WifiSsid:
            output_.println("SSID de Wi-Fi:");
            break;
        case Step::WifiPassword:
            output_.println("Clave de Wi-Fi (vacia para una red abierta):");
            break;
        case Step::ApiUrl:
            output_.println(developmentHttpAllowed_
                                ? "URL base del Device API (https://host:puerto, o http:// solo en red de desarrollo aislada):"
                                : "URL base del Device API (https://host:puerto):");
            break;
        case Step::DeviceCode:
            output_.println("Codigo del dispositivo (deviceCode, no el GUID):");
            break;
        case Step::DeviceKey:
            output_.println("Clave del dispositivo (entregada una sola vez al registrarlo):");
            break;
        case Step::CaCertificate:
            output_.println("Pegue el certificado CA en PEM; termina con la linea -----END CERTIFICATE-----");
            break;
        case Step::Confirm:
            output_.println("Resumen:");
            output_.println("  Wi-Fi: " + draft_.wifiSsid + " (clave " +
                            (draft_.wifiPassword.empty() ? "vacia" : "configurada") + ")");
            output_.println("  Device API: " + draft_.apiBaseUrl);
            output_.println(std::string("  Transporte: ") +
                            (draft_.transport == TransportMode::Https ? "HTTPS con CA" : "HTTP DE DESARROLLO"));
            output_.println("  Dispositivo: " + draft_.deviceCode + " (clave configurada)");
            if (draft_.transport == TransportMode::Https) {
                output_.println("  CA: " + std::to_string(draft_.caCertificatePem.size()) + " bytes");
            }
            output_.println("Escriba 'save' para guardar y reiniciar, o 'cancel'.");
            break;
        case Step::None:
        case Step::ForgetConfirm:
            break;
    }
}

void SerialConsole::handleProvisioning(const std::string& line) {
    switch (step_) {
        case Step::WifiSsid:
            draft_.wifiSsid = line;
            prompt(Step::WifiPassword);
            return;
        case Step::WifiPassword:
            return;
        case Step::ApiUrl: {
            const std::string url = normalizeBaseUrl(line);
            if (startsWith(url, "http://")) {
                if (!developmentHttpAllowed_) {
                    output_.println(configErrorMessage(ConfigError::DevelopmentHttpNotAllowed));
                    prompt(Step::ApiUrl);
                    return;
                }
                draft_.transport = TransportMode::DevelopmentHttp;
                output_.println("AVISO: HTTP sin cifrar. Solo con RequireHttps=false en una red de desarrollo aislada.");
            } else {
                draft_.transport = TransportMode::Https;
            }
            draft_.apiBaseUrl = url;
            prompt(Step::DeviceCode);
            return;
        }
        case Step::DeviceCode:
            draft_.deviceCode = line;
            prompt(Step::DeviceKey);
            return;
        case Step::DeviceKey:
            draft_.deviceKey = line;
            draft_.caCertificatePem.clear();
            prompt(draft_.transport == TransportMode::Https ? Step::CaCertificate : Step::Confirm);
            return;
        case Step::CaCertificate:
            if (line.empty()) {
                return;
            }
            draft_.caCertificatePem += line + "\n";
            if (line.find("-----END CERTIFICATE-----") != std::string::npos) {
                prompt(Step::Confirm);
            }
            return;
        case Step::Confirm: {
            if (line != "save") {
                output_.println("Escriba 'save' o 'cancel'.");
                return;
            }
            const ConfigError error = validateConfig(draft_, developmentHttpAllowed_);
            if (error != ConfigError::None) {
                output_.println(std::string("No se guardo: ") + configErrorMessage(error) + ". Escriba 'provision' de nuevo.");
                clearDraft();
                return;
            }
            if (!configs_.save(draft_)) {
                output_.println("No se pudo guardar en NVS. Escriba 'provision' de nuevo.");
                clearDraft();
                return;
            }
            clearDraft();
            output_.println("Configuracion guardada. Reiniciando...");
            if (hooks_.restart) {
                hooks_.restart();
            }
            return;
        }
        case Step::None:
        case Step::ForgetConfirm:
            return;
    }
}

void SerialConsole::printStatus() {
    output_.println(std::string("configurado: ") + (provisioned_ ? "si" : "no"));
    if (provisioned_) {
        output_.println("Wi-Fi: " + current_.wifiSsid);
        output_.println("Device API: " + current_.apiBaseUrl);
        output_.println(std::string("transporte: ") +
                        (current_.transport == TransportMode::Https ? "HTTPS con CA" : "HTTP DE DESARROLLO"));
        output_.println("dispositivo: " + current_.deviceCode);
        output_.println(std::string("clave del dispositivo: ") + (current_.deviceKey.empty() ? "no configurada" : "configurada"));
    }
    if (hooks_.statusLines) {
        for (const std::string& line : hooks_.statusLines()) {
            output_.println(line);
        }
    }
}

void SerialConsole::printHelp() {
    output_.println("Comandos: provision | status | forget | reconcile | reconcile force | journal discard | restart | help");
}

void SerialConsole::cancel() {
    clearDraft();
    output_.println("Cancelado");
}

void SerialConsole::clearDraft() {
    // Overwrite the secrets before releasing them.
    draft_.wifiPassword.assign(draft_.wifiPassword.size(), '\0');
    draft_.deviceKey.assign(draft_.deviceKey.size(), '\0');
    draft_ = DeviceConfig();
    step_ = Step::None;
}

}  // namespace sciph
