import { useRef } from "react";
import { Camera, ImageIcon, Plus, Upload } from "lucide-react";

import { useIsMobile } from "@/hooks/use-mobile";
import { cn } from "@/lib/utils";

type PhotoPickerActionsProps = {
  onFileSelected: (file: File | undefined) => void;
  hasPhoto?: boolean;
  className?: string;
  /** Compact inline buttons (hero photo) vs full-width prominent buttons (cast picker). */
  size?: "compact" | "prominent";
  prominentLabel?: string;
  prominentSecondaryLabel?: string;
};

export function PhotoPickerActions({
  onFileSelected,
  hasPhoto = false,
  className,
  size = "compact",
  prominentLabel = "Upload a photo",
  prominentSecondaryLabel = "Add another family member",
}: PhotoPickerActionsProps) {
  const isMobile = useIsMobile();
  const libraryRef = useRef<HTMLInputElement>(null);
  const cameraRef = useRef<HTMLInputElement>(null);

  const handleChange = (file: File | undefined) => {
    onFileSelected(file);
    if (libraryRef.current) libraryRef.current.value = "";
    if (cameraRef.current) cameraRef.current.value = "";
  };

  const compactButtonClass =
    "inline-flex items-center gap-1.5 rounded-full bg-primary/10 text-primary px-3 py-1.5 text-xs font-semibold hover:bg-primary/15 transition";

  const prominentButtonClass =
    "w-full rounded-xl border-2 border-dashed border-border bg-background hover:border-primary hover:bg-primary/5 transition px-4 py-4 flex items-center justify-center gap-2 text-sm font-medium text-muted-foreground hover:text-foreground";

  if (size === "prominent") {
    return (
      <div className={cn("space-y-2", className)}>
        {isMobile ? (
          <>
            <button
              type="button"
              onClick={() => cameraRef.current?.click()}
              className={prominentButtonClass}
            >
              <Camera className="h-4 w-4" />
              Take photo
            </button>
            <button
              type="button"
              onClick={() => libraryRef.current?.click()}
              className={prominentButtonClass}
            >
              <ImageIcon className="h-4 w-4" />
              Choose from library
            </button>
          </>
        ) : (
          <button
            type="button"
            onClick={() => libraryRef.current?.click()}
            className={prominentButtonClass}
          >
            {hasPhoto ? (
              <>
                <Plus className="h-4 w-4" />
                {prominentSecondaryLabel}
              </>
            ) : (
              <>
                <Upload className="h-4 w-4" />
                {prominentLabel}
                <span className="text-xs text-muted-foreground font-normal">(optional)</span>
              </>
            )}
          </button>
        )}
        <input
          ref={cameraRef}
          type="file"
          accept="image/*"
          capture="user"
          className="hidden"
          onChange={(e) => handleChange(e.target.files?.[0])}
        />
        <input
          ref={libraryRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={(e) => handleChange(e.target.files?.[0])}
        />
      </div>
    );
  }

  return (
    <div className={cn("flex flex-wrap gap-2", className)}>
      {isMobile ? (
        <>
          <button
            type="button"
            onClick={() => cameraRef.current?.click()}
            className={compactButtonClass}
          >
            <Camera className="h-3.5 w-3.5" />
            Take photo
          </button>
          <button
            type="button"
            onClick={() => libraryRef.current?.click()}
            className={compactButtonClass}
          >
            <ImageIcon className="h-3.5 w-3.5" />
            Choose from library
          </button>
        </>
      ) : (
        <button
          type="button"
          onClick={() => libraryRef.current?.click()}
          className={compactButtonClass}
        >
          <Upload className="h-3.5 w-3.5" />
          {hasPhoto ? "Change photo" : "Upload photo"}
        </button>
      )}
      <input
        ref={cameraRef}
        type="file"
        accept="image/*"
        capture="user"
        className="hidden"
        onChange={(e) => handleChange(e.target.files?.[0])}
      />
      <input
        ref={libraryRef}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={(e) => handleChange(e.target.files?.[0])}
      />
    </div>
  );
}
