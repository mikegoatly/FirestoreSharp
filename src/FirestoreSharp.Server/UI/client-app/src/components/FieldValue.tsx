import type { UiValue } from '../api/types'

interface Props {
  value: UiValue
  inline?: boolean
}

export function FieldValue({ value, inline }: Props) {
  switch (value.type) {
    case 'null':
      return <span className="val val-null">null</span>
    case 'bool':
      return <span className="val val-bool">{String(value.value)}</span>
    case 'int':
      return <span className="val val-int">{String(value.value)}</span>
    case 'double':
      return <span className="val val-double">{String(value.value)}</span>
    case 'string':
      return <span className="val val-string">"{String(value.value)}"</span>
    case 'timestamp':
      return <span className="val val-timestamp">{String(value.value)}</span>
    case 'bytes':
      return <span className="val val-bytes">&lt;bytes&gt;</span>
    case 'reference':
      return <span className="val val-reference">{String(value.value)}</span>
    case 'geopoint': {
      const geo = value.value as { latitude: number; longitude: number }
      return (
        <span className="val val-geopoint">
          ({geo.latitude}, {geo.longitude})
        </span>
      )
    }
    case 'array': {
      const arr = value.value as UiValue[]
      if (inline) return <span className="val val-array">[{arr.length}]</span>
      return (
        <span className="val val-array">
          [{arr.map((v, i) => <FieldValue key={i} value={v} inline />).reduce<React.ReactNode[]>(
            (acc, el, i) => (i === 0 ? [el] : [...acc, ', ', el]),
            []
          )}]
        </span>
      )
    }
    case 'map': {
      const map = value.value as Record<string, UiValue>
      const keys = Object.keys(map)
      if (inline) return <span className="val val-map">&#123;{keys.length}&#125;</span>
      return (
        <span className="val val-map">
          &#123;{keys.join(', ')}&#125;
        </span>
      )
    }
    default:
      return <span className="val">{String(value.value)}</span>
  }
}
