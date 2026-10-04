import { useMemo, useState } from 'react';
import { MailPlus, SearchX, Stethoscope, UserPlus } from 'lucide-react';
import { useCan } from '../../../auth';
import { Button, EmptyState, ErrorState, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { errorMessage, invitationsService, rolesService, usersService } from '../../../services';
import type { Invitation, InvitationFilters as Filters } from '../../../types';
import { InvitationActionDialog, type InvitationActionKind } from '../components/InvitationActionDialog';
import { InvitationDetailDrawer } from '../components/InvitationDetailDrawer';
import { InvitationFilters } from '../components/InvitationFilters';
import { InvitationsTable } from '../components/InvitationsTable';
import { InvitePatientModal } from '../components/InvitePatientModal';
import { InviteStaffModal } from '../components/InviteStaffModal';
import { EMPTY_INVITATION_FILTERS, filterInvitations } from '../helpers';

/** Invitaciones de un solo uso para pacientes (app) y personal (portal). */
export function InvitationsPage() {
  const toast = useToast();
  const canCreate = useCan('invitations', 'create');
  const canManage = useCan('invitations', 'update');
  const { data, loading, error, reload, setData } = useAsync(() => invitationsService.list());
  const rolesQ = useAsync(() => rolesService.list());
  const usersQ = useAsync(() => usersService.list().catch(() => []));
  const invitations = useMemo(() => data ?? [], [data]);

  const [filters, setFilters] = useState<Filters>(EMPTY_INVITATION_FILTERS);
  const q = useDebouncedValue(filters.q);
  const filtered = useMemo(() => filterInvitations(invitations, { ...filters, q }), [invitations, filters, q]);
  const [modal, setModal] = useState<'patient' | 'staff' | null>(null);
  const [detail, setDetail] = useState<Invitation | null>(null);
  const [action, setAction] = useState<{ kind: InvitationActionKind; invitation: Invitation } | null>(null);

  const upsert = (inv: Invitation) =>
    setData((prev) => (prev?.some((x) => x.id === inv.id) ? prev.map((x) => (x.id === inv.id ? inv : x)) : [inv, ...(prev ?? [])]));

  const runAction = async (kind: InvitationActionKind, inv: Invitation) => {
    try {
      const updated = kind === 'resend' ? await invitationsService.reissue(inv.id) : await invitationsService.revoke(inv.id);
      upsert(updated);
      if (detail?.id === updated.id) setDetail(updated);
      toast({
        title: kind === 'resend' ? 'Invitación reenviada' : 'Invitación revocada',
        description:
          kind === 'resend' ? `Enlace nuevo enviado a ${inv.email}. Caduca en 7 días.` : `${inv.email} ya no puede usar el enlace.`,
        tone: 'success',
      });
    } catch (e) {
      toast({ title: kind === 'resend' ? 'No se pudo reenviar' : 'No se pudo revocar', description: errorMessage(e), tone: 'error' });
    } finally {
      setAction(null);
    }
  };

  const onSent = (inv: Invitation) => {
    setModal(null);
    upsert(inv);
    toast({
      title: 'Invitación enviada',
      description: `${inv.email} recibirá el enlace en unos minutos. Caduca en 7 días.`,
      tone: 'success',
    });
  };

  const inviteButtons = canCreate && (
    <>
      <Button variant="secondary" leftIcon={<UserPlus size={16} aria-hidden="true" />} onClick={() => setModal('patient')}>
        Invitar paciente
      </Button>
      <Button leftIcon={<Stethoscope size={16} aria-hidden="true" />} onClick={() => setModal('staff')}>
        Invitar personal
      </Button>
    </>
  );

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Invitaciones"
        description="Enlaces de un solo uso para que pacientes y personal creen su cuenta. Caducan a los 7 días."
        actions={inviteButtons}
      />
      <section aria-label="Lista de invitaciones" className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:p-5">
        <InvitationFilters value={filters} onChange={setFilters} invitations={invitations} resultCount={filtered.length} />
        {error ? (
          <ErrorState
            title="No se pudieron cargar las invitaciones"
            message={`${error} Vuelve a intentarlo en unos segundos.`}
            onRetry={reload}
          />
        ) : (
          <InvitationsTable
            invitations={filtered}
            loading={loading}
            canManage={canManage}
            onOpen={setDetail}
            onResend={(invitation) => setAction({ kind: 'resend', invitation })}
            onRevoke={(invitation) => setAction({ kind: 'revoke', invitation })}
            resetPageKey={`${q}|${filters.tipo}|${filters.estado}`}
            emptyState={
              invitations.length === 0 ? (
                <EmptyState
                  icon={MailPlus}
                  title="Todavía no has enviado invitaciones"
                  description="Invita a un paciente para que consulte su historial en la app, o a personal para que acceda al sistema con su rol."
                  action={inviteButtons}
                />
              ) : (
                <EmptyState
                  icon={SearchX}
                  title="Ninguna invitación coincide con los filtros"
                  action={
                    <Button variant="secondary" onClick={() => setFilters(EMPTY_INVITATION_FILTERS)}>
                      Limpiar filtros
                    </Button>
                  }
                />
              )
            }
          />
        )}
      </section>

      <InvitePatientModal open={modal === 'patient'} onClose={() => setModal(null)} onSent={onSent} />
      <InviteStaffModal open={modal === 'staff'} roles={rolesQ.data ?? []} onClose={() => setModal(null)} onSent={onSent} />
      <InvitationDetailDrawer
        invitation={detail}
        canManage={canManage}
        acceptedUserId={usersQ.data?.find((u) => u.email === detail?.email)?.id}
        onClose={() => setDetail(null)}
        onResend={(invitation) => setAction({ kind: 'resend', invitation })}
        onRevoke={(invitation) => setAction({ kind: 'revoke', invitation })}
      />
      <InvitationActionDialog action={action} onConfirm={runAction} onCancel={() => setAction(null)} />
    </div>
  );
}

export default InvitationsPage;
