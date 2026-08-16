import { createContext, useContext, useId, useState, type ReactNode } from 'react'

/**
 * How wide a detail row has to be to sit under the whole line. Held in a
 * context rather than passed down, because a caller writing the cells of a row
 * should not also have to count them.
 */
const Columns = createContext(1)

/**
 * A list of files as a table: one line each, the same columns down the page,
 * and everything that explains a line folded away until it is asked for.
 *
 * The screens this replaced put four paragraphs under every file, which reads
 * well for one file and is unusable at twenty — and twenty is the ordinary case
 * on the first day. What survives on the line is what someone scanning the page
 * compares between files; what a single file has to say about itself is a
 * sentence they open deliberately, one file at a time.
 */
export default function RowTable({
  heads,
  children,
}: {
  heads: readonly string[]
  children: ReactNode
}) {
  return (
    <div className="rows">
      <table>
        <thead>
          <tr>
            {/* The toggle's own column. Empty rather than headed: it labels
                nothing, and a header over it would be read out as one. */}
            <th className="opener" aria-hidden="true" />
            {heads.map((head) => (
              <th key={head}>{head}</th>
            ))}
          </tr>
        </thead>
        <Columns.Provider value={heads.length}>{children}</Columns.Provider>
      </table>
    </div>
  )
}

/**
 * One file: the name, the cells as `td` children, and what the file has to say
 * for itself under them.
 *
 * `detail` is the target path, the reason it is blocked, what happens to a
 * sidecar — and a row with nothing to add is a row without a toggle rather than
 * one that opens onto nothing. Each row is its own `tbody`, which is what lets
 * the detail be a second `tr` without either row losing its place in the
 * columns.
 */
export function Row({
  name,
  detail,
  children,
}: {
  name: ReactNode
  detail?: ReactNode
  children: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const columns = useContext(Columns)
  const id = useId()
  const opens = detail !== undefined && detail !== null

  return (
    <tbody className={open ? 'open' : undefined}>
      <tr>
        <td className="opener">
          {opens && (
            <button
              type="button"
              className="quiet opener"
              aria-expanded={open}
              aria-controls={id}
              onClick={() => setOpen(!open)}
            >
              <span aria-hidden="true">{open ? '▾' : '▸'}</span>
              <span className="offscreen">{open ? 'Hide details' : 'Show details'}</span>
            </button>
          )}
        </td>

        <th scope="row">{name}</th>

        {children}
      </tr>

      {open && opens && (
        <tr className="detail" id={id}>
          <td />
          <td colSpan={columns}>{detail}</td>
        </tr>
      )}
    </tbody>
  )
}
