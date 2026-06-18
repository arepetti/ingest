import { useMemo, useState } from 'react'
import {
  Badge, Body1, Button, Card, Checkbox, Dropdown, Drawer, DrawerBody, Field, Input,
  Menu, MenuButton, MenuItem, MenuList, MenuPopover, MenuTrigger,
  MessageBarBody, Option, Switch,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Title3, Tooltip,
  makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Add20Regular, ArrowClockwise20Regular, Delete20Regular, Edit20Regular, MoreHorizontal20Regular,
  PauseCircle20Regular, PlayCircle20Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from './AutoScrollMessageBar'
import { DrawerHeaderWithClose } from './DrawerHeaderWithClose'
import { GridMessageRow } from './GridPager'
import { RowActions } from './RowActions'
import { ApprovalPolicyEditor } from './ApprovalPolicyEditor'
import { clickableRowProps } from '../utils/a11y'
import { confirmDelete } from '../utils/confirm'
import { formatApiError } from '../api/client'
import { accountHasCapability } from '../api/capabilities'
import {
  useAccounts, useSchemas, useCapabilities,
  useApprovalRules, useCreateApprovalRule, useUpdateApprovalRule, useDeleteApprovalRule,
} from '../api/hooks'
import type { Account, ApprovalPolicy, ApprovalRule, ApprovalSourceScope, UpsertApprovalRuleRequest } from '../api/types'

const sourceLabels: Record<ApprovalSourceScope, string> = {
  Both: 'manual + API',
  ManualOnly: 'manual only',
  ApiOnly: 'API only',
}

const useStyles = makeStyles({
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  sectionTitle: { display: 'block', marginBottom: '2px' },
  help: { color: tokens.colorNeutralForeground3 },
  titleRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px' },
  headerActions: { display: 'flex', gap: '8px', alignItems: 'center' },
  actions: { display: 'flex', gap: '12px', flexWrap: 'wrap', marginTop: '4px' },
  table: { tableLayout: 'fixed', width: '100%' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  muted: { color: tokens.colorNeutralForeground3 },
  cellTrunc: { maxWidth: 0 },
  colApproval: { width: '180px' },
  colStatus: { width: '100px' },
  colActions: { width: '52px' },
  drawer: { width: 'max(600px, 46vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '14px' },
})

/** Working copy of a rule while it's open in the drawer. "All" is modelled as a checkbox that, when on, clears the id list. */
interface RuleDraft {
  id?: string
  label: string
  enabled: boolean
  allServices: boolean
  serviceIds: string[]
  allSchemas: boolean
  schemaIds: string[]
  policy: ApprovalPolicy
}

function toDraft(rule: ApprovalRule): RuleDraft {
  return {
    id: rule.id,
    label: rule.label ?? '',
    enabled: rule.enabled,
    allServices: rule.serviceIds.length === 0,
    serviceIds: rule.serviceIds,
    allSchemas: rule.schemaIds.length === 0,
    schemaIds: rule.schemaIds,
    policy: rule.policy,
  }
}

function emptyDraft(): RuleDraft {
  return {
    label: '',
    enabled: true,
    allServices: true,
    serviceIds: [],
    allSchemas: true,
    schemaIds: [],
    policy: { mode: 'Required', appliesToSources: 'Both', approvers: [] },
  }
}

/**
 * "Rules" settings subpage. A generic home for cross-cutting rules — today the only kind is an
 * approval rule that requires approval for a chosen set of services and schemas (either side may
 * be "All"), applied on top of the per-schema and global-default policies. A rule scoped to API
 * submissions can be used to force a human to review (and complete) partially-automated feeds
 * before they go live.
 */
export function ApprovalRulesSection() {
  const s = useStyles()
  const { has } = useCapabilities()
  const canManage = has('settings:manage')
  const { data: rules, isLoading, refetch } = useApprovalRules()
  const { data: accountsPage } = useAccounts({ role: 'Service' })
  const { data: approverAccountsPage } = useAccounts()
  const { data: schemasPage } = useSchemas({ pageSize: 200 })
  const del = useDeleteApprovalRule()
  const update = useUpdateApprovalRule()

  const [editing, setEditing] = useState<RuleDraft | null>(null)
  const [banner, setBanner] = useState<{ intent: 'success' | 'error'; text: string } | null>(null)

  const services = useMemo(() => (accountsPage?.items ?? []).filter(a => !a.isDeleted), [accountsPage])
  const servicesById = useMemo(() => new Map(services.map(a => [a.id, a])), [services])
  const schemas = useMemo(() => (schemasPage?.items ?? []), [schemasPage])
  const schemasById = useMemo(() => new Map(schemas.map(sc => [sc.id, sc])), [schemas])
  const approverAccounts = useMemo(
    () => (approverAccountsPage?.items ?? []).filter(a => accountHasCapability(a, 'submissions:approve') && !a.isDeleted),
    [approverAccountsPage],
  )

  function serviceSummary(rule: ApprovalRule): string {
    if (rule.serviceIds.length === 0) return 'All services'
    return rule.serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || '(removed)').join(', ')
  }
  function schemaSummary(rule: ApprovalRule): string {
    if (rule.schemaIds.length === 0) return 'All schemas'
    return rule.schemaIds.map(id => schemasById.get(id)?.label || schemasById.get(id)?.name || '(removed)').join(', ')
  }
  function approvalSummary(rule: ApprovalRule): string {
    if (rule.policy.mode === 'UseGlobalDefault') return 'Use global default'
    if (rule.policy.mode === 'None') return 'No approval'
    return `Required (${sourceLabels[rule.policy.appliesToSources]})`
  }

  async function onDelete(rule: ApprovalRule) {
    if (!confirmDelete('approval rule', rule.label || serviceSummary(rule))) return
    setBanner(null)
    try {
      await del.mutateAsync(rule.id)
      setBanner({ intent: 'success', text: 'Rule deleted.' })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onToggleEnabled(rule: ApprovalRule) {
    setBanner(null)
    const req: UpsertApprovalRuleRequest = {
      label: rule.label ?? null,
      enabled: !rule.enabled,
      serviceIds: rule.serviceIds,
      schemaIds: rule.schemaIds,
      policy: rule.policy,
    }
    try {
      await update.mutateAsync({ id: rule.id, req })
      setBanner({ intent: 'success', text: rule.enabled ? 'Rule disabled.' : 'Rule enabled.' })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  const items = rules ?? []

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <Card className={s.card}>
        <div className={s.titleRow}>
          <Title3 className={s.sectionTitle}>Rules</Title3>
          <div className={s.headerActions}>
            {canManage && (
              <Button appearance="primary" icon={<Add20Regular />} onClick={() => setEditing(emptyDraft())}>
                Add rule
              </Button>
            )}
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>Refresh</MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        </div>
        <Body1 className={s.help}>
          Require approval for specific services and schemas, on top of each schema's own policy.
          Leave a side set to “All” to cover every service or every schema. A rule scoped to API
          submissions can force a person to review and fill in partially-automated feeds before
          they go live.
        </Body1>

        {banner && (
          <AutoScrollMessageBar intent={banner.intent}>
            <MessageBarBody>{banner.text}</MessageBarBody>
          </AutoScrollMessageBar>
        )}

        <Table size="small" className={s.table}>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Label</TableHeaderCell>
              <TableHeaderCell>Services</TableHeaderCell>
              <TableHeaderCell>Schemas</TableHeaderCell>
              <TableHeaderCell className={s.colApproval}>Approval</TableHeaderCell>
              <TableHeaderCell className={s.colStatus}>Status</TableHeaderCell>
              <TableHeaderCell className={s.colActions} aria-label="Actions" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <GridMessageRow colSpan={6}>Loading…</GridMessageRow>}
            {!isLoading && items.length === 0 && (
              <GridMessageRow colSpan={6}>No rules yet{canManage ? ' — click “Add rule” to create one.' : '.'}</GridMessageRow>
            )}
            {items.map(rule => (
              <TableRow
                key={rule.id}
                className={`${s.row} ${s.rowClickable}`}
                {...clickableRowProps(() => canManage && setEditing(toDraft(rule)), `Edit rule ${rule.label || serviceSummary(rule)}`)}
              >
                <TableCell className={s.cellTrunc}>
                  {rule.label
                    ? <strong className={s.truncate}>{rule.label}</strong>
                    : <span className={`${s.truncate} ${s.muted}`}>—</span>}
                </TableCell>
                <TableCell className={s.cellTrunc}>
                  <Tooltip content={serviceSummary(rule)} relationship="label">
                    <span className={s.truncate}>{serviceSummary(rule)}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.cellTrunc}>
                  <Tooltip content={schemaSummary(rule)} relationship="label">
                    <span className={s.truncate}>{schemaSummary(rule)}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.colApproval}>
                  <span className={s.truncate}>{approvalSummary(rule)}</span>
                </TableCell>
                <TableCell className={s.colStatus}>
                  <Badge appearance="outline" color={rule.enabled ? 'success' : 'informative'}>
                    {rule.enabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </TableCell>
                <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                  {canManage && (
                    <RowActions
                      ariaLabel={`Actions for rule ${rule.label || serviceSummary(rule)}`}
                      actions={[
                        { key: 'edit', label: 'Edit', icon: <Edit20Regular />, onClick: () => setEditing(toDraft(rule)) },
                        {
                          key: 'toggle',
                          label: rule.enabled ? 'Disable' : 'Enable',
                          icon: rule.enabled ? <PauseCircle20Regular /> : <PlayCircle20Regular />,
                          disabled: update.isPending,
                          onClick: () => onToggleEnabled(rule),
                        },
                        { key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => onDelete(rule) },
                      ]}
                    />
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      <Drawer
        type="overlay"
        separator
        open={!!editing}
        onOpenChange={(_, d) => { if (!d.open) setEditing(null) }}
        position="end"
        className={s.drawer}
      >
        <DrawerHeaderWithClose
          title={editing?.id ? 'Edit rule' : 'Add rule'}
          onClose={() => setEditing(null)}
        />
        <DrawerBody>
          {editing && (
            <RuleEditor
              key={editing.id ?? 'new'}
              draft={editing}
              services={services}
              schemas={schemas.map(sc => ({ id: sc.id, label: sc.label || sc.name }))}
              approverAccounts={approverAccounts}
              onClose={() => setEditing(null)}
              onSaved={text => { setEditing(null); setBanner({ intent: 'success', text }) }}
            />
          )}
        </DrawerBody>
      </Drawer>
    </div>
  )
}

// --- Rule create / edit form --------------------------------------------------------------

function RuleEditor({
  draft, services, schemas, approverAccounts, onClose, onSaved,
}: {
  draft: RuleDraft
  services: Account[]
  schemas: { id: string; label: string }[]
  approverAccounts: Account[]
  onClose: () => void
  onSaved: (text: string) => void
}) {
  const s = useStyles()
  const isNew = !draft.id
  const create = useCreateApprovalRule()
  const update = useUpdateApprovalRule()

  const [label, setLabel] = useState(draft.label)
  const [enabled, setEnabled] = useState(draft.enabled)
  const [allServices, setAllServices] = useState(draft.allServices)
  const [serviceIds, setServiceIds] = useState<string[]>(draft.serviceIds)
  const [allSchemas, setAllSchemas] = useState(draft.allSchemas)
  const [schemaIds, setSchemaIds] = useState<string[]>(draft.schemaIds)
  const [policy, setPolicy] = useState<ApprovalPolicy>(draft.policy)
  const [error, setError] = useState<string | null>(null)

  const servicesById = useMemo(() => new Map(services.map(a => [a.id, a])), [services])
  const schemasById = useMemo(() => new Map(schemas.map(sc => [sc.id, sc])), [schemas])

  function patchPolicy(patch: Partial<ApprovalPolicy>) {
    setPolicy(prev => ({ ...prev, ...patch }))
  }

  async function onSave() {
    setError(null)
    if (!allServices && serviceIds.length === 0) { setError('Pick at least one service, or choose “All services”.'); return }
    if (!allSchemas && schemaIds.length === 0) { setError('Pick at least one schema, or choose “All schemas”.'); return }
    if (policy.mode === 'Required' && !policy.approvers.some(a => a.requirement === 'Required')) {
      setError('Add at least one Required approver.'); return
    }

    const req: UpsertApprovalRuleRequest = {
      label: label.trim() || null,
      enabled,
      serviceIds: allServices ? [] : serviceIds,
      schemaIds: allSchemas ? [] : schemaIds,
      policy,
    }
    try {
      if (isNew) await create.mutateAsync(req)
      else await update.mutateAsync({ id: draft.id!, req })
      onSaved(isNew ? 'Rule created.' : 'Rule saved.')
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  const pending = create.isPending || update.isPending

  return (
    <div className={s.drawerForm}>
      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}

      <Field label="Label" hint="Optional name shown only in this list.">
        <Input value={label} onChange={(_, d) => setLabel(d.value)} placeholder="e.g. Finance feeds need sign-off" />
      </Field>

      <Switch label="Enabled" checked={enabled} onChange={(_, d) => setEnabled(d.checked)} />

      <Field label="Services">
        <Checkbox label="All services" checked={allServices} onChange={(_, d) => setAllServices(!!d.checked)} />
        {!allServices && (
          <Dropdown
            multiselect
            placeholder="Select services"
            selectedOptions={serviceIds}
            value={serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || id).join(', ')}
            onOptionSelect={(_, d) => setServiceIds(d.selectedOptions)}
          >
            {services.map(a => (
              <Option key={a.id} value={a.id} text={a.label || a.name}>{a.label || a.name}</Option>
            ))}
          </Dropdown>
        )}
      </Field>

      <Field label="Schemas">
        <Checkbox label="All schemas" checked={allSchemas} onChange={(_, d) => setAllSchemas(!!d.checked)} />
        {!allSchemas && (
          <Dropdown
            multiselect
            placeholder="Select schemas"
            selectedOptions={schemaIds}
            value={schemaIds.map(id => schemasById.get(id)?.label || id).join(', ')}
            onOptionSelect={(_, d) => setSchemaIds(d.selectedOptions)}
          >
            {schemas.map(sc => (
              <Option key={sc.id} value={sc.id} text={sc.label}>{sc.label}</Option>
            ))}
          </Dropdown>
        )}
      </Field>

      <ApprovalPolicyEditor
        policy={policy}
        accounts={approverAccounts}
        onChange={patchPolicy}
        heading="Approval"
      />

      <div className={s.actions}>
        <Button appearance="primary" disabled={pending} onClick={onSave}>
          {pending ? 'Saving…' : isNew ? 'Create rule' : 'Save changes'}
        </Button>
        <Button appearance="secondary" disabled={pending} onClick={onClose}>Cancel</Button>
      </div>
    </div>
  )
}
