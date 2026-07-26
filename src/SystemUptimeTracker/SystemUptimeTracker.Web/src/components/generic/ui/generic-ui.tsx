import type {
  ButtonHTMLAttributes,
  CSSProperties,
  ComponentPropsWithoutRef,
  ElementType,
  HTMLAttributes,
  ReactNode,
} from "react";

import Link from "next/link";

const mergeStyles = (baseStyle: CSSProperties, style?: CSSProperties) => ({
  ...baseStyle,
  ...style,
});

type DivProps = HTMLAttributes<HTMLDivElement> & {
  children?: ReactNode;
  style?: CSSProperties;
};

type HeadingProps = HTMLAttributes<HTMLHeadingElement> & {
  children?: ReactNode;
  style?: CSSProperties;
};

type TableSectionProps = HTMLAttributes<
  HTMLTableSectionElement | HTMLTableRowElement
> & {
  children?: ReactNode;
};

type CellProps = HTMLAttributes<HTMLTableCellElement> & {
  children?: ReactNode;
  style?: CSSProperties;
};

type TextProps<T extends ElementType = "p"> = {
  children?: ReactNode;
  renderAs?: T;
  style?: CSSProperties;
} & Omit<ComponentPropsWithoutRef<T>, "children" | "style" | "as">;

type PrimaryButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  children?: ReactNode;
  renderAs?: ElementType;
  style?: CSSProperties;
  href?: string;
  [key: string]: unknown;
};

export function ContentWidth({ children, style, ...props }: DivProps) {
  return (
    <div
      style={mergeStyles(
        {
          inlineSize: "min(100%, 48rem)",
          marginInline: "auto",
        },
        style,
      )}
      {...props}
    >
      {children}
    </div>
  );
}

export function H1({ children, style, ...props }: HeadingProps) {
  return (
    <h1
      style={mergeStyles(
        {
          fontSize: "2rem",
          lineHeight: 1.2,
          marginBlock: "0 1rem",
        },
        style,
      )}
      {...props}
    >
      {children}
    </h1>
  );
}

export function H2({ children, style, ...props }: HeadingProps) {
  return (
    <h2
      style={mergeStyles(
        {
          fontSize: "1.5rem",
          lineHeight: 1.25,
          marginBlock: "0 1rem",
        },
        style,
      )}
      {...props}
    >
      {children}
    </h2>
  );
}

export function H3({ children, style, ...props }: HeadingProps) {
  return (
    <h3
      style={mergeStyles(
        {
          fontSize: "1.25rem",
          lineHeight: 1.3,
          marginBlock: "0 0.75rem",
        },
        style,
      )}
      {...props}
    >
      {children}
    </h3>
  );
}

export function Text<T extends ElementType = "p">({
  children,
  renderAs: Component = "p" as T,
  style,
  ...props
}: TextProps<T>) {
  const RenderComponent = Component as ElementType;

  return (
    <RenderComponent
      style={mergeStyles(
        {
          lineHeight: 1.5,
          marginBlock: "0 1rem",
        },
        style,
      )}
      {...(props as Record<string, unknown>)}
    >
      {children}
    </RenderComponent>
  );
}

export function PrimaryButton({
  children,
  renderAs: Component,
  style,
  type,
  ...props
}: PrimaryButtonProps) {
  const Control = Component || (props.href ? Link : "button");
  const controlProps =
    Control === "button" ? { type: type || "button", ...props } : props;

  return (
    <Control
      style={mergeStyles(
        {
          alignItems: "center",
          backgroundColor: "#1769aa",
          border: "1px solid #1769aa",
          borderRadius: "4px",
          color: "#fff",
          cursor: "pointer",
          display: "inline-flex",
          font: "inherit",
          fontWeight: 600,
          gap: "0.5rem",
          justifyContent: "center",
          minBlockSize: "2.5rem",
          padding: "0.55rem 1rem",
          textDecoration: "none",
        },
        style,
      )}
      {...controlProps}
    >
      {children}
    </Control>
  );
}

export function Table({
  children,
  style,
  ...props
}: HTMLAttributes<HTMLTableElement> & {
  children?: ReactNode;
  style?: CSSProperties;
}) {
  return (
    <div style={{ inlineSize: "100%", overflowX: "auto" }}>
      <table
        style={mergeStyles(
          {
            borderCollapse: "collapse",
            inlineSize: "100%",
            marginBlock: "1rem",
          },
          style,
        )}
        {...props}
      >
        {children}
      </table>
    </div>
  );
}

export function THead({ children, ...props }: TableSectionProps) {
  return <thead {...props}>{children}</thead>;
}

export function TBody({ children, ...props }: TableSectionProps) {
  return <tbody {...props}>{children}</tbody>;
}

export function TR({ children, ...props }: TableSectionProps) {
  return <tr {...props}>{children}</tr>;
}

export function TH({ children, style, ...props }: CellProps) {
  return (
    <th
      style={mergeStyles(
        {
          borderBlockEnd: "2px solid #d0d7de",
          padding: "0.75rem",
          textAlign: "start",
        },
        style,
      )}
      {...props}
    >
      {children}
    </th>
  );
}

export function TD({ children, style, ...props }: CellProps) {
  return (
    <td
      style={mergeStyles(
        {
          borderBlockEnd: "1px solid #d8dee4",
          padding: "0.75rem",
          verticalAlign: "top",
        },
        style,
      )}
      {...props}
    >
      {children}
    </td>
  );
}
