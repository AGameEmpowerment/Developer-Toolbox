package developertoolbox.core;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;

@Entity
@Table(name = "JavaMessages")
public class SampleMessage {
    @Id @org.hibernate.annotations.Nationalized @Column(length = 128) private String id;
    @org.hibernate.annotations.Nationalized @Column(nullable = false, length = 4096) private String text;
    @Column(nullable = false, length = 64) private String source;
    @Column(nullable = false) private Instant createdUtc;
    protected SampleMessage() { }
    public SampleMessage(WorkItem work, String source) {
        this.id = work.id(); this.text = work.text(); this.source = source; this.createdUtc = Instant.now();
    }
    public String getId() { return id; }
    public String getText() { return text; }
}
