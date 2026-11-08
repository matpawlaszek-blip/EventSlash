import cv2
import numpy as np
from metavision_core.event_io import EventsIterator


def main():
    iterator = EventsIterator(input_path="raw_recordings/recording_2026-10-14_11-42-46.raw", delta_t=10000)
    height, width = iterator.get_size()

    print(f"Camera size: {width}x{height}")

    frame = np.full((height, width, 3), 127, dtype=np.uint8)

    cv2.namedWindow("Metavision events", cv2.WINDOW_NORMAL)

    for events in iterator:
        frame[:] = 127

        if events.size:
            x = events["x"]
            y = events["y"]
            p = events["p"].astype(bool)

            frame[y[p], x[p]] = (255, 255, 255)
            frame[y[~p], x[~p]] = (0, 0, 0)

        cv2.imshow("Metavision events", frame)

        key = cv2.waitKey(1) & 0xFF
        if key in (27, ord("q")):
            break

    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()