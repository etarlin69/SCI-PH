#include "feedback/feedback.h"

#include <string>

namespace sciph {

const char* feedbackMessage(FeedbackSignal signal) {
    switch (signal) {
        case FeedbackSignal::Ready:
            return "Lector listo";
        case FeedbackSignal::Provisioning:
            return "Lector sin configurar: escriba 'provision' para configurarlo";
        case FeedbackSignal::PlaceFinger:
            return "Coloque el dedo en el sensor";
        case FeedbackSignal::RemoveFinger:
            return "Retire el dedo";
        case FeedbackSignal::PlaceAgain:
            return "Coloque el mismo dedo otra vez";
        case FeedbackSignal::Success:
            return "Huella leida";
        case FeedbackSignal::NotRecognized:
            return "Huella no reconocida";
        case FeedbackSignal::FingersDoNotMatch:
            return "Las dos capturas no coinciden";
        case FeedbackSignal::LowQuality:
            return "Captura de baja calidad";
        case FeedbackSignal::SensorError:
            return "Error del sensor";
        case FeedbackSignal::SensorFull:
            return "El sensor no tiene espacio libre";
        case FeedbackSignal::NetworkError:
            return "Error de red";
        case FeedbackSignal::AuthenticationFailed:
            return "Autenticacion del lector rechazada";
    }
    return "";
}

LedMode feedbackLedMode(FeedbackSignal signal) {
    switch (signal) {
        case FeedbackSignal::PlaceFinger:
        case FeedbackSignal::RemoveFinger:
        case FeedbackSignal::PlaceAgain:
            return LedMode::On;
        case FeedbackSignal::SensorError:
        case FeedbackSignal::SensorFull:
            return LedMode::BlinkFast;
        case FeedbackSignal::NetworkError:
        case FeedbackSignal::Provisioning:
            return LedMode::BlinkSlow;
        case FeedbackSignal::AuthenticationFailed:
            return LedMode::On;
        default:
            return LedMode::Off;
    }
}

void FeedbackController::signal(FeedbackSignal signal) {
    last_ = signal;
    mode_ = feedbackLedMode(signal);
    output_.println(std::string("> ") + feedbackMessage(signal));
    if (indicator_ == nullptr) {
        return;
    }
    switch (signal) {
        case FeedbackSignal::Success:
            indicator_->beep(80);
            break;
        case FeedbackSignal::NotRecognized:
        case FeedbackSignal::FingersDoNotMatch:
        case FeedbackSignal::LowQuality:
        case FeedbackSignal::SensorError:
        case FeedbackSignal::AuthenticationFailed:
            indicator_->beep(300);
            break;
        default:
            break;
    }
    tick();
}

void FeedbackController::tick() {
    if (indicator_ == nullptr) {
        return;
    }
    bool on = false;
    const uint64_t now = clock_.monotonicMs();
    switch (mode_) {
        case LedMode::Off:
            on = false;
            break;
        case LedMode::On:
            on = true;
            break;
        case LedMode::BlinkSlow:
            on = (now / 1000) % 2 == 0;
            break;
        case LedMode::BlinkFast:
            on = (now / 200) % 2 == 0;
            break;
    }
    if (on != ledOn_) {
        ledOn_ = on;
        indicator_->setLed(on);
    }
}

}  // namespace sciph
