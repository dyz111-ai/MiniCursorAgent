class Student:
    """表示一个学生，包含姓名和成绩列表。"""
    def __init__(self, name: str, scores: list) -> None:
        self.name = name
        self.scores = scores
    
    def get_average(self) -> float:
        """计算平均分，成绩为空时返回0.0。"""
        if not self.scores:
            return 0.0
        return sum(self.scores) / len(self.scores)
    
    def get_grade(self) -> str:
        """根据平均分返回等级：A/B/C/D/F。"""
        avg = self.get_average()
        if avg >= 90:
            return 'A'
        elif avg >= 80:
            return 'B'
        elif avg >= 70:
            return 'C'
        elif avg >= 60:
            return 'D'
        else:
            return 'F'

class Classroom:
    """管理学生集合，提供班级成绩统计和报告功能。"""
    def __init__(self) -> None:
        self.students = []
    
    def add_student(self, student: Student) -> None:
        if not isinstance(student, Student):
            raise TypeError("只能添加 Student 实例")
        self.students.append(student)
    
    def get_class_average(self) -> float:
        """计算全班平均分，无学生时返回0.0。"""
        if not self.students:
            return 0.0
        total = sum(student.get_average() for student in self.students)
        return total / len(self.students)
    
    def get_top_student(self) -> Student | None:
        """返回平均分最高的学生，若无学生则返回 None。"""
        return max(self.students, key=lambda s: s.get_average(), default=None)
    
    def print_report(self) -> None:
        """打印班级成绩报告单。"""
        print("=" * 30)
        print("成绩报告单")
        print("=" * 30)
        for student in self.students:
            avg = student.get_average()
            grade = student.get_grade()
            print(f"{student.name}: 平均分={avg:.1f}, 等级={grade}")
        print("-" * 30)
        top = self.get_top_student()
        if top:
            print(f"第一名: {top.name} ({top.get_average():.1f}分)")
        print(f"班级平均分: {self.get_class_average():.1f}")

# 使用示例
if __name__ == "__main__":
    classroom = Classroom()
    
    # 添加学生
    classroom.add_student(Student("张三", [85, 90, 78]))
    classroom.add_student(Student("李四", [92, 88, 95]))
    classroom.add_student(Student("王五", [70, 65, 72]))
    classroom.add_student(Student("赵六", []))  # 这个学生没成绩，平均分为0.0
    
    # 打印报告
    classroom.print_report()
