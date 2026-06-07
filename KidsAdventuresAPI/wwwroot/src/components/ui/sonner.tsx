import { Toaster as Sonner } from "sonner";

type ToasterProps = React.ComponentProps<typeof Sonner>;

const Toaster = ({ ...props }: ToasterProps) => {
  return (
    <Sonner
      className="adventrya-toaster"
      position="top-center"
      expand
      closeButton
      visibleToasts={2}
      duration={6000}
      offset={0}
      toastOptions={{
        unstyled: true,
        classNames: {
          toast: "adventrya-toast",
          title: "adventrya-toast-title",
          description: "adventrya-toast-description",
          closeButton: "adventrya-toast-close",
          success: "adventrya-toast-success",
          error: "adventrya-toast-error",
          info: "adventrya-toast-info",
          actionButton: "adventrya-toast-action",
          cancelButton: "adventrya-toast-cancel",
        },
      }}
      {...props}
    />
  );
};

export { Toaster };
